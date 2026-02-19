using System;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class AppBuilderExtensionsTests
{
    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_Enables_Loader_And_HotReload_Immediately()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        AvaloniaSourceGeneratedXamlLoader.Enable();
        XamlSourceGenHotReloadManager.Disable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXaml();

        Assert.True(AvaloniaSourceGeneratedXamlLoader.IsEnabled);
        Assert.True(XamlSourceGenHotReloadManager.IsEnabled);
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
