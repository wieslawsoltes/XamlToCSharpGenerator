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
        var installMethod = installerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "Install" && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("Install method was not found.");

        var exception = Record.Exception(() => installMethod.Invoke(null, null));
        if (exception is TargetInvocationException invocationException)
        {
            exception = invocationException.InnerException ?? invocationException;
        }

        Assert.Null(exception);
    }

    [Fact]
    public void Install_Avalonia_Mode_With_Preview_Size_Does_Not_Throw()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var installerType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.SourceGeneratedRuntimeXamlLoaderInstaller",
            throwOnError: true)
            ?? throw new InvalidOperationException("Installer type was not found.");
        var compilerModeType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.PreviewCompilerMode",
            throwOnError: true)
            ?? throw new InvalidOperationException("Preview compiler mode type was not found.");
        var avaloniaMode = Enum.Parse(compilerModeType, "Avalonia");
        var installMethod = installerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
                method.Name == "Install" &&
                method.GetParameters().Length == 3)
            ?? throw new InvalidOperationException("Avalonia overload of Install was not found.");

        var exception = Record.Exception(() => installMethod.Invoke(null, [avaloniaMode, 640d, 480d]));
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

    [Fact]
    public void CreateEvaluatorClassName_Returns_Deterministic_Identifier()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var runtimeType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.SourceGeneratedPreviewMarkupRuntime",
            throwOnError: true)
            ?? throw new InvalidOperationException("Preview markup runtime type was not found.");
        var createNameMethod = runtimeType.GetMethod(
            "CreateEvaluatorClassName",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateEvaluatorClassName was not found.");

        var result = (string?)createNameMethod.Invoke(null, ["source.Quantity + 1"])
            ?? throw new InvalidOperationException("CreateEvaluatorClassName returned null.");

        Assert.StartsWith("__AXSGPreviewExpr_", result, StringComparison.Ordinal);
        Assert.Matches("^__AXSGPreviewExpr_[A-F0-9]+$", result);
        Assert.Equal(
            result,
            (string?)createNameMethod.Invoke(null, ["source.Quantity + 1"]));
    }
}
