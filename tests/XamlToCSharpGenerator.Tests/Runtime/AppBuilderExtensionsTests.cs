using System;
using System.Reflection;
using Avalonia;
using Avalonia.Markup.Xaml;
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
    public void UseAvaloniaSourceGeneratedXaml_Registers_Avalonia_Runtime_Loader_Bridge()
    {
        SourceGenRuntimeXamlLoaderBridge.ResetForTests();
        AppContext.SetSwitch("XamlToCSharpGenerator.Runtime.DisableDynamicBridge", false);
        var builder = AppBuilder.Configure<TestApp>();
        builder.UseAvaloniaSourceGeneratedXaml();
        builder.AfterSetupCallback(builder);

        var runtimeLoaderInterface = typeof(AvaloniaXamlLoader)
            .GetNestedType("IRuntimeXamlLoader", BindingFlags.NonPublic);
        Assert.NotNull(runtimeLoaderInterface);

        var currentResolver = typeof(AvaloniaLocator)
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);
        Assert.NotNull(currentResolver);

        var getServiceMethod = currentResolver!.GetType()
            .GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance, [typeof(Type)]);
        Assert.NotNull(getServiceMethod);

        var runtimeLoader = getServiceMethod!.Invoke(currentResolver, [runtimeLoaderInterface!]);
        Assert.NotNull(runtimeLoader);
        Assert.Equal(
            SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.RegisteredDynamicProxy,
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
    public void UseAvaloniaSourceGeneratedXaml_RuntimeLoaderBridge_Invokes_Without_MethodAccessException()
    {
        var snapshot = AvaloniaSourceGeneratedXamlLoader.RuntimeCompilationOptions;
        XamlSourceGenRegistry.Clear();
        try
        {
            AvaloniaSourceGeneratedXamlLoader.ConfigureRuntimeCompilation(options =>
            {
                options.EnableRuntimeCompilationFallback = false;
            });

            var builder = AppBuilder.Configure<TestApp>();
            builder.UseAvaloniaSourceGeneratedXaml();
            builder.AfterSetupCallback(builder);

            var runtimeLoaderInterface = typeof(AvaloniaXamlLoader)
                .GetNestedType("IRuntimeXamlLoader", BindingFlags.NonPublic);
            Assert.NotNull(runtimeLoaderInterface);

            var currentResolver = typeof(AvaloniaLocator)
                .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            Assert.NotNull(currentResolver);

            var getServiceMethod = currentResolver!.GetType()
                .GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance, [typeof(Type)]);
            Assert.NotNull(getServiceMethod);

            var runtimeLoader = getServiceMethod!.Invoke(currentResolver, [runtimeLoaderInterface!]);
            Assert.NotNull(runtimeLoader);

            var loadMethod = runtimeLoaderInterface!.GetMethod("Load");
            Assert.NotNull(loadMethod);

            var document = new RuntimeXamlLoaderDocument(
                new Uri("avares://RuntimeTests/BridgeMissing.axaml"),
                "<TextBlock xmlns='https://github.com/avaloniaui' Text='Missing' />");

            var invocation = Assert.Throws<TargetInvocationException>(() =>
                loadMethod!.Invoke(runtimeLoader, [document, new RuntimeXamlLoaderConfiguration()]));
            Assert.NotNull(invocation.InnerException);
            Assert.False(invocation.InnerException is MethodAccessException);
            Assert.IsType<XamlLoadException>(invocation.InnerException);
        }
        finally
        {
            XamlSourceGenRegistry.Clear();
            AvaloniaSourceGeneratedXamlLoader.ConfigureRuntimeCompilation(options =>
            {
                options.EnableRuntimeCompilationFallback = snapshot.EnableRuntimeCompilationFallback;
                options.CacheCompiledDocuments = snapshot.CacheCompiledDocuments;
                options.UseCompiledBindingsByDefault = snapshot.UseCompiledBindingsByDefault;
                options.CreateSourceInfo = snapshot.CreateSourceInfo;
                options.StrictMode = snapshot.StrictMode;
                options.CSharpExpressionsEnabled = snapshot.CSharpExpressionsEnabled;
                options.ImplicitCSharpExpressionsEnabled = snapshot.ImplicitCSharpExpressionsEnabled;
                options.AllowImplicitXmlnsDeclaration = snapshot.AllowImplicitXmlnsDeclaration;
                options.ImplicitStandardXmlnsPrefixesEnabled = snapshot.ImplicitStandardXmlnsPrefixesEnabled;
                options.ImplicitDefaultXmlns = snapshot.ImplicitDefaultXmlns;
                options.TraceDiagnostics = snapshot.TraceDiagnostics;
            });
        }
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
        Assert.True(status.IsEnabled);
        Assert.False(status.Options.PersistChangesToSource);
        Assert.False(status.Options.WaitForHotReload);
    }

    [Fact]
    public void UseAvaloniaSourceGeneratedXamlHotDesign_Disables_Manager()
    {
        XamlSourceGenHotDesignManager.Enable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlHotDesign(enable: false);
        builder.AfterSetupCallback(builder);

        Assert.False(XamlSourceGenHotDesignManager.IsEnabled);
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
