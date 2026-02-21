using System;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace SourceGenXamlCatalogSample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            .UseAvaloniaSourceGeneratedRuntimeXamlCompilation(enable: true, configure: options =>
            {
                options.TraceDiagnostics = true;
            })
            .UseAvaloniaSourceGeneratedXamlHotDesign(configure: options =>
            {
                options.PersistChangesToSource = true;
                options.WaitForHotReload = true;
                options.HotReloadWaitTimeout = TimeSpan.FromSeconds(10);
            })
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);
    }
}
