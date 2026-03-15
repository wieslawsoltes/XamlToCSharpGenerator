using System.Reflection;
using global::Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public class SourceGeneratedDesignerHostTests
{
    [Fact]
    public void Install_Registers_RuntimeLoader_Override_Without_Throwing()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var installerType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.SourceGeneratedRuntimeXamlLoaderInstaller",
            throwOnError: true)
            ?? throw new InvalidOperationException("Installer type was not found.");
        var installMethod = installerType.GetMethod(
            "Install",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Install method was not found.");

        var exception = Record.Exception(() => installMethod.Invoke(null, null));
        if (exception is TargetInvocationException invocationException)
        {
            exception = invocationException.InnerException ?? invocationException;
        }

        Assert.Null(exception);
    }

    [Fact]
    public void ProxyFactory_Invokes_Load_Delegate_Without_MethodAccess_Failure()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var factoryType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.RuntimeXamlLoaderProxyFactory",
            throwOnError: true)
            ?? throw new InvalidOperationException("Proxy factory type was not found.");
        var createMethod = factoryType.GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Create method was not found.");
        var loaderContractType = typeof(AvaloniaXamlLoader).Assembly.GetType(
            "Avalonia.Markup.Xaml.AvaloniaXamlLoader+IRuntimeXamlLoader",
            throwOnError: true)
            ?? throw new InvalidOperationException("Avalonia runtime XAML loader contract was not found.");
        var expected = new object();
        Func<RuntimeXamlLoaderDocument, RuntimeXamlLoaderConfiguration, object> loadHandler =
            (_, _) => expected;

        var proxy = createMethod.Invoke(null, [loaderContractType, loadHandler])
            ?? throw new InvalidOperationException("Create returned null.");
        var loadMethod = loaderContractType.GetMethod(
            "Load",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Load method was not found.");

        var result = loadMethod.Invoke(proxy, [null, null]);

        Assert.Same(expected, result);
    }
}
