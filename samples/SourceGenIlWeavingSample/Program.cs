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
        var enableIdePollingFallback = !Debugger.IsAttached;

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: enableIdePollingFallback, pollingIntervalMs: 1000);
    }
}
