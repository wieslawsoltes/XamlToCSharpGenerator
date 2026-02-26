using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlPropertyTokenSemanticsTests
{
    [Theory]
    [InlineData("Setter.Value", "Setter", "Value")]
    [InlineData("Grid.Row", "Grid", "Row")]
    [InlineData("  Owner.Property  ", "Owner", "Property")]
    public void TrySplitOwnerQualifiedProperty_ReturnsOwnerAndProperty(
        string token,
        string expectedOwner,
        string expectedProperty)
    {
        var result = XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(token, out var owner, out var property);

        Assert.True(result);
        Assert.Equal(expectedOwner, owner);
        Assert.Equal(expectedProperty, property);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Value")]
    [InlineData(".Value")]
    [InlineData("Owner.")]
    public void TrySplitOwnerQualifiedProperty_RejectsInvalidTokens(string token)
    {
        var result = XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(token, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void IsPropertyElementName_MatchesQualifiedAndUnqualifiedNames()
    {
        Assert.True(XamlPropertyTokenSemantics.IsPropertyElementName("Setter.Value", "Value", "Setter"));
        Assert.True(XamlPropertyTokenSemantics.IsPropertyElementName("MergedDictionaries", "MergedDictionaries"));
        Assert.True(XamlPropertyTokenSemantics.IsPropertyElementName("ResourceDictionary.Styles", "Styles"));
    }

    [Fact]
    public void IsPropertyElementName_RequiresMatchingOwnerWhenProvided()
    {
        Assert.False(XamlPropertyTokenSemantics.IsPropertyElementName("Another.Value", "Value", "Setter"));
        Assert.False(XamlPropertyTokenSemantics.IsPropertyElementName("Value", "Value", "Setter"));
        Assert.False(XamlPropertyTokenSemantics.IsPropertyElementName("Setter.Other", "Value", "Setter"));
    }
}
