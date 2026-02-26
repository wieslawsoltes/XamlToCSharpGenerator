using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlAccessibilityModifierSemanticsTests
{
    [Theory]
    [InlineData("public", "public")]
    [InlineData("PUBLIC", "public")]
    [InlineData("protected   internal", "protected internal")]
    [InlineData(" private    protected ", "private protected")]
    [InlineData("notpublic", "internal")]
    public void NormalizeClassModifier_Normalizes_Known_Values(string input, string expected)
    {
        Assert.Equal(expected, XamlAccessibilityModifierSemantics.NormalizeClassModifier(input));
    }

    [Theory]
    [InlineData("private", "private")]
    [InlineData("PUBLIC", "public")]
    [InlineData("NotPublic", "internal")]
    public void NormalizeFieldModifier_Normalizes_Known_Values(string input, string expected)
    {
        Assert.Equal(expected, XamlAccessibilityModifierSemantics.NormalizeFieldModifier(input));
    }

    [Fact]
    public void NormalizeClassModifier_Returns_Null_For_Unknown_Modifier()
    {
        Assert.Null(XamlAccessibilityModifierSemantics.NormalizeClassModifier("file"));
    }
}
