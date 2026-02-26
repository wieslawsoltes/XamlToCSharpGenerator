using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlListValueSemanticsTests
{
    [Fact]
    public void SplitWhitespaceAndCommaTokens_Splits_Class_Like_Tokens()
    {
        var tokens = XamlListValueSemantics.SplitWhitespaceAndCommaTokens("h2, hero\tlarge\nheadline");

        Assert.Equal(new[] { "h2", "hero", "large", "headline" }, tokens.ToArray());
    }

    [Fact]
    public void SplitCommaSeparatedTokens_Splits_And_Trims_Items()
    {
        var tokens = XamlListValueSemantics.SplitCommaSeparatedTokens("One, Two ,Three");

        Assert.Equal(new[] { "One", "Two", "Three" }, tokens.ToArray());
    }

    [Fact]
    public void SplitCommaSeparatedTokens_Preserves_Quoted_Comma_Content()
    {
        var tokens = XamlListValueSemantics.SplitCommaSeparatedTokens("One,\"Two,Three\",'Four,Five'");

        Assert.Equal(new[] { "One", "Two,Three", "Four,Five" }, tokens.ToArray());
    }
}
