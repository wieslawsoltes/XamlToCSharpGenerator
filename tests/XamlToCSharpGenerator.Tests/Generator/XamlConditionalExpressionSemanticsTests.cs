using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlConditionalExpressionSemanticsTests
{
    [Fact]
    public void TryParse_Parses_ApiInformation_Prefixed_Method_Call()
    {
        var ok = XamlConditionalExpressionSemantics.TryParse(
            "ApiInformation.IsTypePresent('Avalonia.Controls.TextBlock')",
            out var expression,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal("IsTypePresent", expression.MethodName);
        Assert.Equal("Avalonia.Controls.TextBlock", Assert.Single(expression.Arguments));
    }

    [Fact]
    public void TryParse_Preserves_Commas_Inside_Quoted_Arguments()
    {
        var ok = XamlConditionalExpressionSemantics.TryParse(
            "IsMethodPresent('Demo.Type', 'M,WithComma')",
            out var expression,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal(2, expression.Arguments.Length);
        Assert.Equal("M,WithComma", expression.Arguments[1]);
    }

    [Fact]
    public void TryParse_Rejects_Unsupported_Method()
    {
        var ok = XamlConditionalExpressionSemantics.TryParse(
            "ApiInformation.IsThingPresent('Avalonia.Controls.TextBlock')",
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("Unsupported conditional method", errorMessage);
    }

    [Fact]
    public void TryParse_Rejects_Empty_Argument()
    {
        var ok = XamlConditionalExpressionSemantics.TryParse(
            "IsMethodPresent('Demo.Type', )",
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("empty argument", errorMessage);
    }

    [Fact]
    public void TryParse_Rejects_Invalid_Arity()
    {
        var ok = XamlConditionalExpressionSemantics.TryParse(
            "IsPropertyPresent('Demo.Type')",
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("expects 2 argument", errorMessage);
    }

    [Fact]
    public void TryParseMethodCallShape_Accepts_Unsupported_Method_Name()
    {
        var ok = XamlConditionalExpressionSemantics.TryParseMethodCallShape(
            "ApiInformation.IsThingPresent('Avalonia.Controls.TextBlock')",
            out var normalizedExpression,
            out var methodName,
            out var argumentsText,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal("ApiInformation.IsThingPresent('Avalonia.Controls.TextBlock')", normalizedExpression);
        Assert.Equal("IsThingPresent", methodName);
        Assert.Equal("'Avalonia.Controls.TextBlock'", argumentsText);
    }
}
