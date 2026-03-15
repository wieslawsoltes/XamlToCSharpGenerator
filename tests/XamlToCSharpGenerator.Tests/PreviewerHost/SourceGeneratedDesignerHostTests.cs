using System.Reflection;

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
}
