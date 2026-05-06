using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace EditorAvaloniaBrowserSample;

internal static class Program
{
    private static Task Main(string[] args)
    {
        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .WithInterFont();
    }
}
