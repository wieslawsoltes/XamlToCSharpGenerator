using System;
using System.Diagnostics;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace SourceGenIlWeavingSample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var enableIdePollingFallback = !(OperatingSystem.IsMacOS() && Debugger.IsAttached);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            // macOS debugger sessions are unstable when the sample's IDE polling fallback timer is active.
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: enableIdePollingFallback, pollingIntervalMs: 1000);
    }
}
