using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlPropertyElementSemanticsTests
{
    [Fact]
    public void IsPropertyElementName_Recognizes_OwnerQualified_Elements()
    {
        Assert.True(XamlPropertyElementSemantics.IsPropertyElementName("Grid.RowDefinitions"));
        Assert.True(XamlPropertyElementSemantics.IsPropertyElementName("ToolTip.Tip"));
    }

    [Fact]
    public void IsPropertyElementName_Rejects_NonOwnerQualified_Elements()
    {
        Assert.False(XamlPropertyElementSemantics.IsPropertyElementName("RowDefinitions"));
        Assert.False(XamlPropertyElementSemantics.IsPropertyElementName("Grid."));
        Assert.False(XamlPropertyElementSemantics.IsPropertyElementName(".RowDefinitions"));
    }

    [Fact]
    public void IsAttachedPropertyToken_Matches_Property_Element_Semantics()
    {
        Assert.True(XamlPropertyElementSemantics.IsAttachedPropertyToken("DockPanel.Dock"));
        Assert.False(XamlPropertyElementSemantics.IsAttachedPropertyToken("Classes"));
    }
}
