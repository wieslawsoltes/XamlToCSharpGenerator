using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlWhitespaceTokenSemanticsTests
{
    [Fact]
    public void SplitTokens_Splits_On_Mixed_Whitespace()
    {
        var tokens = XamlWhitespaceTokenSemantics.SplitTokens("d  mc\tcustom\nx");

        Assert.Equal(4, tokens.Length);
        Assert.Equal("d", tokens[0]);
        Assert.Equal("mc", tokens[1]);
        Assert.Equal("custom", tokens[2]);
        Assert.Equal("x", tokens[3]);
    }

    [Fact]
    public void SplitTokens_Returns_Empty_For_Null_Or_Whitespace()
    {
        Assert.Empty(XamlWhitespaceTokenSemantics.SplitTokens(null));
        Assert.Empty(XamlWhitespaceTokenSemantics.SplitTokens(" \t\r\n "));
    }

    [Theory]
    [InlineData("RootPanel", false)]
    [InlineData("Root Panel", true)]
    [InlineData("Root\tPanel", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsWhitespace_Returns_Expected_Result(string? value, bool expected)
    {
        Assert.Equal(expected, XamlWhitespaceTokenSemantics.ContainsWhitespace(value));
    }
}
