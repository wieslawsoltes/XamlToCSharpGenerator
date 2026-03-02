using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class AppBuilderExtensionsTests
{
    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_DoesNotEnable_HotReload_Before_Setup()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Disable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXaml();

        Assert.False(XamlSourceGenHotReloadManager.IsEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_Registers_AfterSetup_Callback_For_Loader_And_HotReload()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        AvaloniaSourceGeneratedXamlLoader.Enable();
        XamlSourceGenHotReloadManager.Disable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXaml();
        builder.AfterSetupCallback(builder);

        Assert.True(AvaloniaSourceGeneratedXamlLoader.IsEnabled);
        Assert.True(XamlSourceGenHotReloadManager.IsEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_Registers_Avalonia_Runtime_Loader_Bridge()
    {
        SourceGenRuntimeXamlLoaderBridge.ResetForTests();
        AppContext.SetSwitch("XamlToCSharpGenerator.Runtime.DisableDynamicBridge", false);
        var builder = AppBuilder.Configure<TestApp>();
        builder.UseAvaloniaSourceGeneratedXaml();
        builder.AfterSetupCallback(builder);

        Assert.False(SourceGenRuntimeXamlLoaderBridge.IsRegistered);
        Assert.Equal(
            SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.RuntimeLoaderInterfaceMissing,
            SourceGenRuntimeXamlLoaderBridge.RegistrationStatus);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_RuntimeLoaderBridge_Uses_Explicit_Aot_Safe_Path_When_Dynamic_Bridge_Disabled()
    {
        SourceGenRuntimeXamlLoaderBridge.ResetForTests();
        AppContext.SetSwitch("XamlToCSharpGenerator.Runtime.DisableDynamicBridge", true);

        try
        {
            var builder = AppBuilder.Configure<TestApp>();
            builder.UseAvaloniaSourceGeneratedXaml();
            builder.AfterSetupCallback(builder);

            Assert.False(SourceGenRuntimeXamlLoaderBridge.IsRegistered);
            Assert.Equal(
                SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.DynamicBridgeDisabledBySwitch,
                SourceGenRuntimeXamlLoaderBridge.RegistrationStatus);
        }
        finally
        {
            AppContext.SetSwitch("XamlToCSharpGenerator.Runtime.DisableDynamicBridge", false);
            SourceGenRuntimeXamlLoaderBridge.ResetForTests();
        }
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_RuntimeLoaderBridge_Remains_AotSafe_Without_Dynamic_Proxy()
    {
        SourceGenRuntimeXamlLoaderBridge.ResetForTests();
        AppContext.SetSwitch("XamlToCSharpGenerator.Runtime.DisableDynamicBridge", false);
        var builder = AppBuilder.Configure<TestApp>();
        builder.UseAvaloniaSourceGeneratedXaml();
        builder.AfterSetupCallback(builder);

        Assert.False(SourceGenRuntimeXamlLoaderBridge.IsRegistered);
        Assert.Equal(
            SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.RuntimeLoaderInterfaceMissing,
            SourceGenRuntimeXamlLoaderBridge.RegistrationStatus);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedRuntimeXamlCompilation_Configures_Fallback()
    {
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedRuntimeXamlCompilation(enable: true, configure: options =>
        {
            options.CreateSourceInfo = true;
            options.TraceDiagnostics = true;
        });
        builder.AfterSetupCallback(builder);

        var options = AvaloniaSourceGeneratedXamlLoader.RuntimeCompilationOptions;
        Assert.True(options.EnableRuntimeCompilationFallback);
        Assert.True(options.CreateSourceInfo);
        Assert.True(options.TraceDiagnostics);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlHotReload_Disables_Manager()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Enable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlHotReload(enable: false);
        builder.AfterSetupCallback(builder);

        Assert.False(XamlSourceGenHotReloadManager.IsEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlHotReload_Enables_Manager()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Disable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlHotReload(enable: true);
        builder.AfterSetupCallback(builder);

        Assert.True(XamlSourceGenHotReloadManager.IsEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback_Enables_Fallback_Manager()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 250);
        builder.AfterSetupCallback(builder);

        Assert.True(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback_Disables_Fallback_Manager()
    {
        XamlSourceGenHotReloadManager.EnableIdePollingFallback(intervalMs: 250);
        XamlSourceGenHotReloadManager.ClearRegistrations();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: false);
        builder.AfterSetupCallback(builder);

        Assert.False(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_AutoEnables_IdeFallback_From_Environment()
    {
        var original = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");

        try
        {
            XamlSourceGenHotReloadManager.DisableIdePollingFallback();
            XamlSourceGenHotReloadManager.ClearRegistrations();
            var builder = AppBuilder.Configure<TestApp>();

            builder.UseAvaloniaSourceGeneratedXaml();
            builder.AfterSetupCallback(builder);

            Assert.True(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);
        }
        finally
        {
            XamlSourceGenHotReloadManager.DisableIdePollingFallback();
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", original);
        }
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlHotReloadHandler_Registers_Handler_For_Reload_Pipeline()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.ResetHandlersToDefaults();
        var builder = AppBuilder.Configure<TestApp>();
        var handler = new BuilderTestHotReloadHandler();

        builder.UseAvaloniaSourceGeneratedXamlHotReloadHandler(handler);
        builder.AfterSetupCallback(builder);

        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.Register(new BuilderTestReloadTarget(), _ => { });
        XamlSourceGenHotReloadManager.UpdateApplication([typeof(BuilderTestReloadTarget)]);

        Assert.True(handler.ReloadCompletedCount > 0);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlHotDesign_Enables_Manager_With_Configuration()
    {
        XamlSourceGenStudioHost.Stop();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlHotDesign(configure: options =>
        {
            options.PersistChangesToSource = false;
            options.WaitForHotReload = false;
        });
        builder.AfterSetupCallback(builder);

        var status = XamlSourceGenHotDesignManager.GetStatus();
        var studioSnapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(status.IsEnabled);
        Assert.False(status.Options.PersistChangesToSource);
        Assert.False(status.Options.WaitForHotReload);
        Assert.False(XamlSourceGenStudioHost.IsStarted);
        Assert.False(studioSnapshot.IsEnabled);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlHotDesign_Disables_Manager()
    {
        XamlSourceGenStudioHost.Stop();
        XamlSourceGenHotDesignManager.Enable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlHotDesign(enable: false);
        builder.AfterSetupCallback(builder);

        Assert.False(XamlSourceGenHotDesignManager.IsEnabled);
        Assert.False(XamlSourceGenStudioHost.IsStarted);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedStudio_Starts_StudioHost_With_Provided_Options()
    {
        XamlSourceGenStudioHost.Stop();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedStudio(options =>
        {
            options.ShowOverlayIndicator = false;
            options.EnableExternalWindow = false;
            options.WaitMode = SourceGenStudioWaitMode.None;
            options.FallbackPolicy = SourceGenStudioFallbackPolicy.NoFallback;
        });
        builder.AfterSetupCallback(builder);

        var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(XamlSourceGenStudioHost.IsStarted);
        Assert.True(snapshot.IsEnabled);
        Assert.Equal(SourceGenStudioWaitMode.None, snapshot.Options.WaitMode);
        Assert.Equal(SourceGenStudioFallbackPolicy.NoFallback, snapshot.Options.FallbackPolicy);

        XamlSourceGenStudioHost.Stop();
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedStudioFromEnvironment_Does_Not_Start_When_Disabled()
    {
        XamlSourceGenStudioHost.Stop();
        using var environmentScope = new EnvironmentVariableScope(
            "AXSG_STUDIO_ENABLE",
            "AXSG_STUDIO");

        Environment.SetEnvironmentVariable("AXSG_STUDIO_ENABLE", null);
        Environment.SetEnvironmentVariable("AXSG_STUDIO", null);
        var builder = AppBuilder.Configure<TestApp>();
        builder.UseAvaloniaSourceGeneratedStudioFromEnvironment();
        builder.AfterSetupCallback(builder);

        Assert.False(XamlSourceGenStudioHost.IsStarted);
        Assert.False(XamlSourceGenStudioManager.GetStatusSnapshot().IsEnabled);
        XamlSourceGenStudioHost.Stop();
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedStudioFromEnvironment_Starts_And_Applies_Remote_Options()
    {
        XamlSourceGenStudioHost.Stop();
        using var environmentScope = new EnvironmentVariableScope(
            "AXSG_STUDIO_ENABLE",
            "AXSG_STUDIO",
            "AXSG_STUDIO_REMOTE_ENABLE",
            "AXSG_STUDIO_REMOTE_HOST",
            "AXSG_STUDIO_REMOTE_PORT",
            "AXSG_STUDIO_VNC_ENDPOINT",
            "AXSG_STUDIO_OVERLAY_INDICATOR",
            "AXSG_STUDIO_EXTERNAL_WINDOW");
        var remotePort = AllocateTcpPort();

        Environment.SetEnvironmentVariable("AXSG_STUDIO_ENABLE", "1");
        Environment.SetEnvironmentVariable("AXSG_STUDIO_REMOTE_ENABLE", "1");
        Environment.SetEnvironmentVariable("AXSG_STUDIO_REMOTE_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("AXSG_STUDIO_REMOTE_PORT", remotePort.ToString());
        Environment.SetEnvironmentVariable("AXSG_STUDIO_VNC_ENDPOINT", "vnc://127.0.0.1:5900");
        Environment.SetEnvironmentVariable("AXSG_STUDIO_OVERLAY_INDICATOR", "0");
        Environment.SetEnvironmentVariable("AXSG_STUDIO_EXTERNAL_WINDOW", "0");

        var builder = AppBuilder.Configure<TestApp>();
        builder.UseAvaloniaSourceGeneratedStudioFromEnvironment();
        builder.AfterSetupCallback(builder);

        var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(XamlSourceGenStudioHost.IsStarted);
        Assert.True(snapshot.IsEnabled);
        Assert.True(snapshot.Options.EnableRemoteDesign);
        Assert.Equal("127.0.0.1", snapshot.Options.RemoteHost);
        Assert.Equal(remotePort, snapshot.Options.RemotePort);
        Assert.Equal("vnc://127.0.0.1:5900", snapshot.Options.VncEndpoint);
        Assert.False(snapshot.Options.ShowOverlayIndicator);
        Assert.False(snapshot.Options.EnableExternalWindow);

        XamlSourceGenStudioHost.Stop();
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedStudioFromEnvironment_Starts_When_Legacy_Enable_Variable_Is_Set()
    {
        XamlSourceGenStudioHost.Stop();
        using var environmentScope = new EnvironmentVariableScope(
            "AXSG_STUDIO_ENABLE",
            "AXSG_STUDIO");

        Environment.SetEnvironmentVariable("AXSG_STUDIO_ENABLE", null);
        Environment.SetEnvironmentVariable("AXSG_STUDIO", "1");

        var builder = AppBuilder.Configure<TestApp>();
        builder.UseAvaloniaSourceGeneratedStudioFromEnvironment();
        builder.AfterSetupCallback(builder);

        Assert.True(XamlSourceGenStudioHost.IsStarted);
        Assert.True(XamlSourceGenStudioManager.GetStatusSnapshot().IsEnabled);
        XamlSourceGenStudioHost.Stop();
    }

    private static int AllocateTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        listener.Stop();
        return endpoint.Port;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public EnvironmentVariableScope(params string[] names)
        {
            _originalValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < names.Length; index++)
            {
                var name = names[index];
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }

    private sealed class TestApp : Application
    {
    }

    private sealed class BuilderTestReloadTarget
    {
    }

    private sealed class BuilderTestHotReloadHandler : ISourceGenHotReloadHandler
    {
        public int ReloadCompletedCount { get; private set; }

        public void ReloadCompleted(SourceGenHotReloadUpdateContext context)
        {
            ReloadCompletedCount++;
        }
    }
}
