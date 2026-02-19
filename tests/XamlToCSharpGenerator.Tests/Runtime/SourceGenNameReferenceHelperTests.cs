using Avalonia.Controls;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenNameReferenceHelperTests
{
    [Fact]
    public void ResolveByName_Returns_Value_From_Direct_NameScope()
    {
        var nameScope = new NameScope();
        var target = new object();
        nameScope.Register("Target", target);

        var resolved = SourceGenNameReferenceHelper.ResolveByName(nameScope, "Target");

        Assert.Same(target, resolved);
    }

    [Fact]
    public void ResolveByName_Returns_Value_From_StyledElement_NameScope()
    {
        var nameScope = new NameScope();
        var target = new object();
        nameScope.Register("Target", target);

        var border = new Border();
        NameScope.SetNameScope(border, nameScope);

        var resolved = SourceGenNameReferenceHelper.ResolveByName(border, "Target");

        Assert.Same(target, resolved);
    }
}
