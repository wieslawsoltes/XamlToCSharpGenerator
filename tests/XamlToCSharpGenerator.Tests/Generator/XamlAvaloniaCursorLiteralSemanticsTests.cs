using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlAvaloniaCursorLiteralSemanticsTests
{
    [Theory]
    [InlineData("Arrow", "Arrow")]
    [InlineData("Hand", "Hand")]
    [InlineData("StandardCursorType.Cross", "Cross")]
    [InlineData("CursorType.Ibeam", "Ibeam")]
    [InlineData("  StandardCursorType.Wait  ", "Wait")]
    public void TryParseStandardCursorTypeMember_Parses_Supported_Tokens(string literal, string expectedMember)
    {
        var ok = XamlAvaloniaCursorLiteralSemantics.TryParseStandardCursorTypeMember(literal, out var member);

        Assert.True(ok);
        Assert.Equal(expectedMember, member);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("custom://cursor")]
    [InlineData("StandardCursorType.")]
    [InlineData("OtherOwner.Hand")]
    [InlineData("Hand Pointer")]
    public void TryParseStandardCursorTypeMember_Rejects_Invalid_Shapes(string literal)
    {
        var ok = XamlAvaloniaCursorLiteralSemantics.TryParseStandardCursorTypeMember(literal, out _);

        Assert.False(ok);
    }
}
