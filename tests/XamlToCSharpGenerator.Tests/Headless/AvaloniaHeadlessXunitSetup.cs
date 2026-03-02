using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(XamlToCSharpGenerator.Tests.Headless.AvaloniaHeadlessTestApplication))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]

namespace XamlToCSharpGenerator.Tests.Headless;

public sealed class AvaloniaHeadlessTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<AvaloniaHeadlessTestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }
}
