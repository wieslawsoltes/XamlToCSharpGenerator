using Foundation;
using UIKit;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using XamlToCSharpGenerator.Runtime;

namespace ControlCatalog
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the 
    // User Interface of the application, as well as listening (and optionally responding) to 
    // application events from iOS.
    [Register("AppDelegate")]
    public partial class AppDelegate : AvaloniaAppDelegate<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            LogHotReloadEnvironment();

            return base.CustomizeAppBuilder(builder)
                .UseAvaloniaSourceGeneratedXaml()
                .UseAvaloniaSourceGeneratedStudioFromEnvironment()
#if DEBUG
                .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000)
#endif
                .AfterSetup(_ =>
                {
                    Pages.EmbedSample.Implementation = new EmbedSampleIOS();
                });
        }

        private static void LogHotReloadEnvironment()
        {
            var trace = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRACE");
            if (string.IsNullOrWhiteSpace(trace))
            {
                return;
            }

            var message =
                "[AXSG.iOS.Env] DOTNET_WATCH_HOTRELOAD_NAMEDPIPE_NAME=" +
                (Environment.GetEnvironmentVariable("DOTNET_WATCH_HOTRELOAD_NAMEDPIPE_NAME") ?? "<null>") +
                ", DOTNET_HOTRELOAD_NAMEDPIPE_NAME=" +
                (Environment.GetEnvironmentVariable("DOTNET_HOTRELOAD_NAMEDPIPE_NAME") ?? "<null>") +
                ", DOTNET_MODIFIABLE_ASSEMBLIES=" +
                (Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES") ?? "<null>") +
                ", DOTNET_STARTUP_HOOKS=" +
                (Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS") ?? "<null>") +
                ", AXSG_HOTRELOAD_REMOTE_ENDPOINT=" +
                (Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_REMOTE_ENDPOINT") ?? "<null>") +
                ", AXSG_HOTRELOAD_REMOTE_ENDPOINT_EXPLICIT=" +
                (Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_REMOTE_ENDPOINT_EXPLICIT") ?? "<null>") +
                ", AXSG_DOTNET_WATCH_PIPE_DIRECTORY=" +
                (Environment.GetEnvironmentVariable("AXSG_DOTNET_WATCH_PIPE_DIRECTORY") ?? "<null>");

            Console.WriteLine(message);
        }
    }
}
