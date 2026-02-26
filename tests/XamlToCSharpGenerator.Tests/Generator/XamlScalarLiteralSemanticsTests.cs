using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlScalarLiteralSemanticsTests
{
    [Theory]
    [InlineData("null")]
    [InlineData(" NULL ")]
    public void IsNullLiteral_Recognizes_Null_Token(string token)
    {
        Assert.True(XamlScalarLiteralSemantics.IsNullLiteral(token));
    }

    [Fact]
    public void TryParseBoolean_Parses_Trimmed_Value()
    {
        Assert.True(XamlScalarLiteralSemantics.TryParseBoolean(" true ", out var value));
        Assert.True(value);
    }

    [Fact]
    public void TryParseInt32_Uses_Invariant_Culture()
    {
        Assert.True(XamlScalarLiteralSemantics.TryParseInt32(" 42 ", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryParseInt64_Uses_Invariant_Culture()
    {
        Assert.True(XamlScalarLiteralSemantics.TryParseInt64(" 922337203685477580 ", out var value));
        Assert.Equal(922337203685477580L, value);
    }

    [Fact]
    public void TryParseSingle_Uses_Invariant_Culture()
    {
        Assert.True(XamlScalarLiteralSemantics.TryParseSingle(" 1.25 ", out var value));
        Assert.Equal(1.25f, value);
    }

    [Fact]
    public void TryParseDouble_Uses_Invariant_Culture()
    {
        Assert.True(XamlScalarLiteralSemantics.TryParseDouble(" 3.5 ", out var value));
        Assert.Equal(3.5d, value);
    }

    [Fact]
    public void TryParseDecimal_Uses_Invariant_Culture()
    {
        Assert.True(XamlScalarLiteralSemantics.TryParseDecimal(" 19.95 ", out var value));
        Assert.Equal(19.95m, value);
    }
}
