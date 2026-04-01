using System;
using Avalonia;
using ReactiveUI.Avalonia;

namespace EditorAvaloniaSample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI(_ => { })
            .WithInterFont()
            .LogToTrace();
    }
}
