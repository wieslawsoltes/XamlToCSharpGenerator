using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.Tests.Generator;

public class SelectorPropertyPredicateSemanticsTests
{
    [Fact]
    public void TrySplitPredicate_Splits_At_Top_Level_Equals()
    {
        var success = SelectorPropertyPredicateSemantics.TrySplitPredicate(
            "Tag={Binding Path=Name, Converter={x:Static local:C.D}}",
            out var propertyText,
            out var valueText);

        Assert.True(success);
        Assert.Equal("Tag", propertyText);
        Assert.Equal("{Binding Path=Name, Converter={x:Static local:C.D}}", valueText);
    }

    [Theory]
    [InlineData("(controls|Grid.Row)", SelectorAttachedPropertyParseKind.ValidAttached, "controls:Grid", "Row")]
    [InlineData("Tag", SelectorAttachedPropertyParseKind.NotAttached, "", "")]
    [InlineData("(Grid|)", SelectorAttachedPropertyParseKind.InvalidAttached, "", "")]
    [InlineData("(controls|Grid.Row) extra", SelectorAttachedPropertyParseKind.InvalidAttached, "", "")]
    public void TryParseAttachedPropertyToken_Returns_Expected_Result(
        string propertyText,
        SelectorAttachedPropertyParseKind expectedKind,
        string expectedOwnerTypeToken,
        string expectedPropertyName)
    {
        var kind = SelectorPropertyPredicateSemantics.TryParseAttachedPropertyToken(
            propertyText,
            out var ownerTypeToken,
            out var propertyName);

        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedOwnerTypeToken, ownerTypeToken);
        Assert.Equal(expectedPropertyName, propertyName);
    }
}
