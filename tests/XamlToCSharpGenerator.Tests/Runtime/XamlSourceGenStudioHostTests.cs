using Avalonia.Controls;
using XamlToCSharpGenerator.Runtime;
using Xunit;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class XamlSourceGenStudioHostTests
{
    [Fact]
    public void ResolveLiveSurfaceDataContextBinding_Uses_Window_Source_For_Inherited_Content_DataContext()
    {
        var windowRoot = new Grid();
        var content = new Grid();

        var result = XamlSourceGenStudioHost.ResolveLiveSurfaceDataContextBinding(windowRoot, content);

        Assert.Same(windowRoot, result.Source);
        Assert.Null(result.InitialValue);
    }

    [Fact]
    public void ResolveLiveSurfaceDataContextBinding_Uses_InitialValue_For_Explicit_Content_DataContext()
    {
        var windowRoot = new Grid();
        var expectedDataContext = new object();
        var content = new Grid
        {
            DataContext = expectedDataContext
        };

        var result = XamlSourceGenStudioHost.ResolveLiveSurfaceDataContextBinding(windowRoot, content);

        Assert.Null(result.Source);
        Assert.Same(expectedDataContext, result.InitialValue);
    }
}
