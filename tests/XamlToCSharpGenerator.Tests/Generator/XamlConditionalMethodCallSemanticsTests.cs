using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlConditionalMethodCallSemanticsTests
{
    [Fact]
    public void TryParseMethodCall_Parses_Prefixed_Method_And_Arguments()
    {
        var ok = XamlConditionalMethodCallSemantics.TryParseMethodCall(
            " ApiInformation.IsMethodPresent('Demo.Type', 'M') ",
            out var normalizedExpression,
            out var methodName,
            out var argumentsText,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal("ApiInformation.IsMethodPresent('Demo.Type', 'M')", normalizedExpression);
        Assert.Equal("IsMethodPresent", methodName);
        Assert.Equal("'Demo.Type', 'M'", argumentsText);
    }

    [Fact]
    public void TryParseMethodCall_Rejects_Trailing_Tokens_After_Method_Call()
    {
        var ok = XamlConditionalMethodCallSemantics.TryParseMethodCall(
            "IsTypePresent('Demo.Type') && true",
            out _,
            out _,
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("must be a method call", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseMethodCall_Rejects_Empty_Method_Name()
    {
        var ok = XamlConditionalMethodCallSemantics.TryParseMethodCall(
            "ApiInformation.('Demo.Type')",
            out _,
            out _,
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("must be a method call", errorMessage, StringComparison.Ordinal);
    }
}
