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

    [Fact]
    public void TrySplitOwnerQualifiedPropertyFragment_Allows_Empty_Property_Suffix()
    {
        Assert.True(XamlPropertyElementSemantics.TrySplitOwnerQualifiedPropertyFragment("Window.", out var ownerToken, out var propertyFragment));
        Assert.Equal("Window", ownerToken);
        Assert.Equal(string.Empty, propertyFragment);
    }

    [Fact]
    public void TrySplitOwnerQualifiedPropertyFragment_Rejects_Missing_Owner()
    {
        Assert.False(XamlPropertyElementSemantics.TrySplitOwnerQualifiedPropertyFragment(".Opacity", out _, out _));
    }

    [Fact]
    public void IsOwnerSegmentOffset_Identifies_Owner_And_Property_Parts()
    {
        Assert.True(XamlPropertyElementSemantics.IsOwnerSegmentOffset("Path.Opacity", 2));
        Assert.False(XamlPropertyElementSemantics.IsOwnerSegmentOffset("Path.Opacity", 5));
        Assert.False(XamlPropertyElementSemantics.IsOwnerSegmentOffset("Path.Opacity", 8));
    }
}
