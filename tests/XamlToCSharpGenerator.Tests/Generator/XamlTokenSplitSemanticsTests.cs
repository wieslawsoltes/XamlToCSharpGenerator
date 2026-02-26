using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlTokenSplitSemanticsTests
{
    [Theory]
    [InlineData("Owner.Property", '.', "Owner", "Property")]
    [InlineData("a.b.c", '.', "a.b", "c")]
    public void TrySplitAtLastSeparator_SplitsExpectedSegments(
        string token,
        char separator,
        string expectedLeft,
        string expectedRight)
    {
        var result = XamlTokenSplitSemantics.TrySplitAtLastSeparator(
            token,
            separator,
            out var left,
            out var right);

        Assert.True(result);
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedRight, right);
    }

    [Theory]
    [InlineData("prefix:type", ':', "prefix", "type")]
    [InlineData("min-width:42", ':', "min-width", "42")]
    public void TrySplitAtFirstSeparator_SplitsExpectedSegments(
        string token,
        char separator,
        string expectedLeft,
        string expectedRight)
    {
        var result = XamlTokenSplitSemantics.TrySplitAtFirstSeparator(
            token,
            separator,
            out var left,
            out var right);

        Assert.True(result);
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedRight, right);
    }

    [Theory]
    [InlineData("", '.')]
    [InlineData("NoSeparator", '.')]
    [InlineData(".Value", '.')]
    [InlineData("Owner.", '.')]
    public void SplitHelpers_RejectInvalidTokens(string token, char separator)
    {
        var first = XamlTokenSplitSemantics.TrySplitAtFirstSeparator(token, separator, out _, out _);
        var last = XamlTokenSplitSemantics.TrySplitAtLastSeparator(token, separator, out _, out _);

        Assert.False(first);
        Assert.False(last);
    }

    [Theory]
    [InlineData("WidthProperty", "Property", "Width")]
    [InlineData(" WidthProperty ", "Property", "Width")]
    [InlineData("Property", "Property", "Property")]
    [InlineData("Width", "Property", "Width")]
    public void TrimTerminalSuffix_TrimsOnlyWhenTokenHasLeadingPayload(
        string token,
        string suffix,
        string expected)
    {
        var result = XamlTokenSplitSemantics.TrimTerminalSuffix(token, suffix);

        Assert.Equal(expected, result);
    }
}
