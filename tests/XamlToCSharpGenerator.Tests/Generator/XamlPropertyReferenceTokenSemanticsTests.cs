using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlPropertyReferenceTokenSemanticsTests
{
    [Theory]
    [InlineData("Grid.Row", "Grid.Row")]
    [InlineData("(Grid.Row)", "Grid.Row")]
    [InlineData("  ( Grid.Row )  ", "Grid.Row")]
    [InlineData("  Grid.Row  ", "Grid.Row")]
    public void TryNormalize_Normalizes_Property_Reference_Tokens(string rawToken, string expected)
    {
        Assert.True(XamlPropertyReferenceTokenSemantics.TryNormalize(rawToken, out var normalizedToken));
        Assert.Equal(expected, normalizedToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("()")]
    [InlineData("(   )")]
    public void TryNormalize_Rejects_Empty_Tokens(string rawToken)
    {
        Assert.False(XamlPropertyReferenceTokenSemantics.TryNormalize(rawToken, out _));
    }
}
