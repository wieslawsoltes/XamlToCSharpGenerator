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
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);

        if (!IsStudioEnabled())
        {
            return builder;
        }

        return builder.UseAvaloniaSourceGeneratedStudio(options =>
        {
            options.PersistChangesToSource = true;
            options.WaitMode = SourceGenStudioWaitMode.WaitForLocalOnly;
            options.UpdateTimeout = TimeSpan.FromSeconds(10);
            options.FallbackPolicy = SourceGenStudioFallbackPolicy.RuntimeApplyOnTimeout;
            options.ShowOverlayIndicator = true;
            options.EnableExternalWindow = true;
            options.AutoOpenStudioWindowOnStartup = false;
        });
    }

    private static bool IsStudioEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AXSG_STUDIO");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
