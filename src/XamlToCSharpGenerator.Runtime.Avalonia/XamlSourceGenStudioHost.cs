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
        lock (Sync)
        {
            if (!Started)
            {
                return;
            }

            Started = false;
            XamlSourceGenStudioManager.StudioStatusChanged -= OnStudioStatusChanged;
            XamlSourceGenStudioManager.Disable();
        }

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
        SourceGenStudioStatusSnapshot snapshot;
        lock (Sync)
        {
            if (!Started || !ActiveOptions.EnableExternalWindow)
            {
                return;
            }

            snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (DashboardWindow is null || !DashboardWindow.IsVisible)
            {
                DashboardWindow = new StudioDashboardWindow();
            }

            DashboardWindow.Update(snapshot);
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

        Dispatcher.UIThread.Post(() =>
        {
            if (DashboardWindow is not null && DashboardWindow.IsVisible)
            {
                DashboardWindow.Update(snapshot);
            }
        }, DispatcherPriority.Background);
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
                IndicatorWindow = new StudioIndicatorWindow
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
        private readonly TextBlock _statusText;

        public StudioIndicatorWindow()
        {
            Width = 280;
            Height = 54;
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
                Padding = new Thickness(10, 8),
                Child = (_statusText = new TextBlock
                {
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                })
            };
        }

        public void Update(SourceGenStudioStatusSnapshot snapshot)
        {
            _statusText.Text = "Studio: " + snapshot.CurrentState +
                               " | Session: " + (snapshot.SessionId == Guid.Empty ? "<none>" : snapshot.SessionId.ToString("N")[..8]) +
                               " | Ops: " + snapshot.Operations.Count;
        }
    }

    private sealed class StudioDashboardWindow : Window
    {
        private readonly TextBlock _statusText;

        public StudioDashboardWindow()
        {
            Width = 760;
            Height = 460;
            MinWidth = 600;
            MinHeight = 360;
            Title = "SourceGen Hot Design Studio";
            Content = new Border
            {
                Padding = new Thickness(12),
                Child = (_statusText = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = FontFamily.Default,
                    FontSize = 13
                })
            };
        }

        public void Update(SourceGenStudioStatusSnapshot snapshot)
        {
            var lastOperation = snapshot.Operations.Count > 0
                ? snapshot.Operations[^1]
                : null;
            var scopeCount = snapshot.ActiveScopeCount;
            var lastMessage = lastOperation?.Result?.Message ?? "No operations yet.";

            _statusText.Text =
                "Studio Session: " + (snapshot.SessionId == Guid.Empty ? "<none>" : snapshot.SessionId) + Environment.NewLine +
                "State: " + snapshot.CurrentState + Environment.NewLine +
                "Registered documents: " + snapshot.RegisteredDocumentCount + Environment.NewLine +
                "Scopes: " + scopeCount + Environment.NewLine +
                "Operation history: " + snapshot.Operations.Count + Environment.NewLine + Environment.NewLine +
                "Latest result:" + Environment.NewLine +
                lastMessage;
        }
    }
}
