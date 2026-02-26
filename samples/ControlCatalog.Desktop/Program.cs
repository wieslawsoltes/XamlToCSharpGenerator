using System;
using Avalonia;
using ControlCatalog.NetCore;
using ControlCatalog.Pages;
using XamlToCSharpGenerator.Runtime;

namespace ControlCatalog
{
    internal class Program
    {
        [STAThread]
        public static int Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        /// <summary>
        /// This method is needed for IDE previewer infrastructure
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UseAvaloniaSourceGeneratedXaml()
                .LogToTrace()
                .AfterSetup(builder =>
                {
                    EmbedSample.Implementation = new EmbedSampleWin();
                })
                .UsePlatformDetect();
    }
}
