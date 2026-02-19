using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class AppBuilderExtensionsTests
{
    [Fact]
    public void UseAvaloniaSourceGeneratedXaml_Enables_Loader_And_HotReload_Immediately()
    {
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
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Disable();
        var builder = AppBuilder.Configure<TestApp>();

        builder.UseAvaloniaSourceGeneratedXamlHotReload(enable: true);
        builder.AfterSetupCallback(builder);

        Assert.True(XamlSourceGenHotReloadManager.IsEnabled);
    }

    private sealed class TestApp : Application
    {
    }
}
