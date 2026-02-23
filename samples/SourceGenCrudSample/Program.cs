using System;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace SourceGenCrudSample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ExceptionLogging.InstallGlobalHandlers();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            .UseAvaloniaSourceGeneratedXamlHotDesign(configure: options =>
            {
                options.PersistChangesToSource = true;
                options.WaitForHotReload = true;
                options.HotReloadWaitTimeout = TimeSpan.FromSeconds(10);
            })
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);
    }
}
