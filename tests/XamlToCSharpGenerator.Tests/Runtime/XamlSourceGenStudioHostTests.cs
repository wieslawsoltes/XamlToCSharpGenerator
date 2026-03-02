using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class XamlSourceGenStudioHostTests
{
    [AvaloniaFact]
    public void ResolveLiveSurfaceDataContextBinding_Uses_Window_Source_For_Inherited_Content_DataContext()
    {
        var windowRoot = new Grid();
        var content = new Grid();

        var result = XamlSourceGenStudioHost.ResolveLiveSurfaceDataContextBinding(windowRoot, content);

        Assert.Same(windowRoot, result.Source);
        Assert.Null(result.InitialValue);
    }

    [AvaloniaFact]
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
