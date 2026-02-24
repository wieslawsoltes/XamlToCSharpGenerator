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
            .UseAvaloniaSourceGeneratedStudio(options =>
            {
                options.PersistChangesToSource = true;
                options.WaitMode = SourceGenStudioWaitMode.WaitForLocalOnly;
                options.UpdateTimeout = TimeSpan.FromSeconds(10);
                options.FallbackPolicy = SourceGenStudioFallbackPolicy.RuntimeApplyOnTimeout;
                options.ShowOverlayIndicator = true;
                options.EnableExternalWindow = true;
                options.AutoOpenStudioWindowOnStartup = true;
            })
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);
    }
}
