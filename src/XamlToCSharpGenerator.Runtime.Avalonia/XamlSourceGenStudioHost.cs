using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenStudioHost
{
    private static readonly object Sync = new();

    private static SourceGenStudioOptions ActiveOptions = new();
    private static StudioIndicatorWindow? IndicatorWindow;
    private static StudioDashboardWindow? DashboardWindow;
    private static XamlSourceGenStudioShellViewModel? ShellViewModel;
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

        EnsureIndicatorWindow(snapshot);
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
                HorizontalAlignment = HorizontalAlignment.Left
            };
            openButton.Click += (_, _) => openStudioAction();
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
}
