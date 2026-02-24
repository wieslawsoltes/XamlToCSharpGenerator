using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Windows.Input;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenStudioHost
{
    private static readonly object Sync = new();

    private static SourceGenStudioOptions ActiveOptions = new();
    private static StudioIndicatorWindow? IndicatorWindow;
    private static StudioDashboardWindow? DashboardWindow;
    private static XamlSourceGenStudioShellViewModel? ShellViewModel;
    private static readonly Dictionary<Window, StudioOverlayAttachment> OverlayAttachments = new();
    private static readonly HashSet<Window> TrackedWindows = new();
    private static DispatcherTimer? OverlayRefreshTimer;
    private static bool Started;

    public static bool IsStarted
    {
        get
        {
            lock (Sync)
            {
                return Started;
            }
        }
    }

    public static void Start(SourceGenStudioOptions? options = null)
    {
        SourceGenStudioStatusSnapshot snapshot;
        lock (Sync)
        {
            ActiveOptions = options?.Clone() ?? ActiveOptions.Clone();
            if (Started)
            {
                return;
            }

            Started = true;
            XamlSourceGenStudioManager.Enable(ActiveOptions);
            XamlSourceGenStudioManager.StartSession();
            ShellViewModel = new XamlSourceGenStudioShellViewModel(ActiveOptions);
            XamlSourceGenStudioManager.StudioStatusChanged += OnStudioStatusChanged;
            snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        }

        if (ActiveOptions.ShowOverlayIndicator)
        {
            EnsureIndicatorWindow(snapshot);
        }

        EnsureOverlayRefreshTimer();
        AttachStudioOverlayToTopLevels();

        if (ActiveOptions.AutoOpenStudioWindowOnStartup && ActiveOptions.EnableExternalWindow)
        {
            OpenStudioWindow();
        }
    }

    public static void Stop()
    {
        XamlSourceGenStudioShellViewModel? shellViewModel;
        lock (Sync)
        {
            if (!Started)
            {
                return;
            }

            Started = false;
            XamlSourceGenStudioManager.StudioStatusChanged -= OnStudioStatusChanged;
            shellViewModel = ShellViewModel;
            ShellViewModel = null;
            XamlSourceGenStudioManager.Disable();
        }

        shellViewModel?.Dispose();

        Dispatcher.UIThread.Post(static () =>
        {
            DetachStudioOverlayFromTopLevels();
            StopOverlayRefreshTimer();
            DetachTrackedWindows();

            if (IndicatorWindow is not null)
            {
                IndicatorWindow.Close();
                IndicatorWindow = null;
            }

            if (DashboardWindow is not null)
            {
                DashboardWindow.Close();
                DashboardWindow = null;
            }
        }, DispatcherPriority.Background);
    }

    public static void OpenStudioWindow()
    {
        XamlSourceGenStudioShellViewModel? shellViewModel;
        lock (Sync)
        {
            if (!Started || !ActiveOptions.EnableExternalWindow)
            {
                return;
            }

            shellViewModel = ShellViewModel;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (DashboardWindow is null || !DashboardWindow.IsVisible)
            {
                DashboardWindow = new StudioDashboardWindow(shellViewModel);
            }
            DashboardWindow.Show();
            DashboardWindow.Activate();
        }, DispatcherPriority.Background);
    }

    private static void OnStudioStatusChanged(SourceGenStudioStatusSnapshot snapshot)
    {
        if (!IsStarted)
        {
            return;
        }

        AttachStudioOverlayToTopLevels();
        EnsureIndicatorWindow(snapshot);
    }

    private static void AttachStudioOverlayToTopLevels()
    {
        Dispatcher.UIThread.Post(static () =>
        {
            if (!Started || ShellViewModel is null)
            {
                return;
            }

            foreach (var window in EnumerateStudioEligibleWindows())
            {
                TrackWindow(window);
                TryAttachOverlay(window);
            }
        }, DispatcherPriority.Background);
    }

    private static void TryAttachOverlay(Window window)
    {
        if (OverlayAttachments.ContainsKey(window))
        {
            return;
        }

        if (!IsStudioEligibleWindow(window))
        {
            return;
        }

        if (window.Content is null || window.Content is XamlSourceGenStudioOverlayView)
        {
            return;
        }

        var originalContent = window.Content;
        var dataContextBinding = ResolveLiveSurfaceDataContextBinding(window, originalContent);

        window.Content = null;

        var overlay = new XamlSourceGenStudioOverlayView(
            originalContent,
            dataContextBinding.Source,
            dataContextBinding.InitialValue)
        {
            DataContext = ShellViewModel
        };
        window.Content = overlay;

        OverlayAttachments[window] = new StudioOverlayAttachment(window, originalContent, overlay);
    }

    internal static (StyledElement? Source, object? InitialValue) ResolveLiveSurfaceDataContextBinding(
        StyledElement defaultDataContextSource,
        object? originalContent)
    {
        if (originalContent is StyledElement originalStyledElement &&
            originalStyledElement.IsSet(StyledElement.DataContextProperty))
        {
            // Preserve explicit content DataContext without creating a circular binding
            // between the live presenter and the moved content root.
            return (null, originalStyledElement.DataContext);
        }

        return (defaultDataContextSource, null);
    }

    private static void DetachStudioOverlayFromTopLevels()
    {
        foreach (var entry in OverlayAttachments.Values)
        {
            if (entry.Window is null)
            {
                continue;
            }

            if (!ReferenceEquals(entry.Window.Content, entry.OverlayContent))
            {
                continue;
            }

            try
            {
                entry.Window.Content = null;
                entry.Window.Content = entry.OriginalContent;
            }
            catch
            {
                // Best effort cleanup for windows that are closing/closed.
            }
        }

        OverlayAttachments.Clear();
    }

    private static bool IsStudioEligibleWindow(Window window)
    {
        return !ReferenceEquals(window, IndicatorWindow) &&
               !ReferenceEquals(window, DashboardWindow);
    }

    private static void EnsureOverlayRefreshTimer()
    {
        Dispatcher.UIThread.Post(static () =>
        {
            if (OverlayRefreshTimer is not null)
            {
                return;
            }

            OverlayRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, static (_, _) =>
            {
                AttachStudioOverlayToTopLevels();
            });
            OverlayRefreshTimer.Start();
        }, DispatcherPriority.Background);
    }

    private static void StopOverlayRefreshTimer()
    {
        if (OverlayRefreshTimer is null)
        {
            return;
        }

        OverlayRefreshTimer.Stop();
        OverlayRefreshTimer = null;
    }

    private static void TrackWindow(Window? window)
    {
        if (window is null || !TrackedWindows.Add(window))
        {
            return;
        }

        window.Closed += OnTrackedWindowClosed;
    }

    private static void UntrackWindow(Window? window)
    {
        if (window is null || !TrackedWindows.Remove(window))
        {
            return;
        }

        try
        {
            window.Closed -= OnTrackedWindowClosed;
        }
        catch
        {
            // Best effort unsubscribe only.
        }
    }

    private static void DetachTrackedWindows()
    {
        foreach (var window in TrackedWindows)
        {
            try
            {
                window.Closed -= OnTrackedWindowClosed;
            }
            catch
            {
                // Best effort unsubscribe only.
            }
        }

        TrackedWindows.Clear();
    }

    private static void OnTrackedWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        OverlayAttachments.Remove(window);
        UntrackWindow(window);
    }

    private static IEnumerable<Window> EnumerateStudioEligibleWindows()
    {
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            yield break;
        }

        for (var index = 0; index < desktopLifetime.Windows.Count; index++)
        {
            var window = desktopLifetime.Windows[index];
            if (window is null)
            {
                continue;
            }

            if (!IsStudioEligibleWindow(window))
            {
                continue;
            }

            yield return window;
        }
    }

    private static void EnsureIndicatorWindow(SourceGenStudioStatusSnapshot snapshot)
    {
        lock (Sync)
        {
            if (!ActiveOptions.ShowOverlayIndicator || !Started)
            {
                return;
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (IndicatorWindow is null || !IndicatorWindow.IsVisible)
            {
                IndicatorWindow = new StudioIndicatorWindow(static () => OpenStudioWindow())
                {
                    Position = new PixelPoint(16, 16)
                };
                IndicatorWindow.Show();
            }

            IndicatorWindow.Update(snapshot);
        }, DispatcherPriority.Background);
    }

    private sealed class StudioIndicatorWindow : Window
    {
        private readonly TextBlock? _statusText;

        public StudioIndicatorWindow(Action openStudioAction)
        {
            Width = 280;
            Height = 72;
            CanResize = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromArgb(230, 16, 22, 36));
            Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 57, 109, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8)
            };

            if (Content is not Border border)
            {
                return;
            }

            var layout = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                RowSpacing = 6
            };

            layout.Children.Add(_statusText = new TextBlock
            {
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            var openButton = new Button
            {
                Content = "Open Studio",
                HorizontalAlignment = HorizontalAlignment.Left,
                Command = new StudioActionCommand(openStudioAction)
            };
            Grid.SetRow(openButton, 1);
            layout.Children.Add(openButton);

            border.Child = layout;
        }

        public void Update(SourceGenStudioStatusSnapshot snapshot)
        {
            if (_statusText is null)
            {
                return;
            }

            _statusText.Text = "Studio: " + snapshot.CurrentState +
                               " | Session: " + (snapshot.SessionId == Guid.Empty ? "<none>" : snapshot.SessionId.ToString("N")[..8]) +
                               " | Ops: " + snapshot.Operations.Count;
        }
    }

    private sealed class StudioDashboardWindow : Window
    {
        public StudioDashboardWindow(XamlSourceGenStudioShellViewModel? shellViewModel)
        {
            Width = 1280;
            Height = 840;
            MinWidth = 960;
            MinHeight = 640;
            Title = "SourceGen Hot Design Studio";

            if (shellViewModel is null)
            {
                Content = new TextBlock
                {
                    Margin = new Thickness(12),
                    Text = "Studio view model is not available."
                };
                return;
            }

            Content = new XamlSourceGenStudioShellView
            {
                DataContext = shellViewModel
            };
        }
    }

    private sealed record StudioOverlayAttachment(
        Window Window,
        object? OriginalContent,
        XamlSourceGenStudioOverlayView OverlayContent);

    private sealed class StudioActionCommand(Action execute) : ICommand
    {
        private readonly Action _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }
    }
}
