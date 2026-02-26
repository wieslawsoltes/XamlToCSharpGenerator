using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlConditionalNamespaceUriSemanticsTests
{
    [Fact]
    public void TrySplit_Splits_Conditional_Namespace_At_Top_Level_Question_Mark()
    {
        var ok = XamlConditionalNamespaceUriSemantics.TrySplit(
            "using:Demo.Namespace?IsTypePresent(\"Demo.Type\")",
            out var normalizedNamespace,
            out var conditionExpression);

        Assert.True(ok);
        Assert.Equal("using:Demo.Namespace", normalizedNamespace);
        Assert.Equal("IsTypePresent(\"Demo.Type\")", conditionExpression);
    }

    [Fact]
    public void TrySplit_Does_Not_Split_On_Question_Mark_Inside_Quoted_Argument()
    {
        var ok = XamlConditionalNamespaceUriSemantics.TrySplit(
            "using:Demo.Namespace?IsMethodPresent('Demo.Type','Name?WithQuestion')",
            out var normalizedNamespace,
            out var conditionExpression);

        Assert.True(ok);
        Assert.Equal("using:Demo.Namespace", normalizedNamespace);
        Assert.Equal("IsMethodPresent('Demo.Type','Name?WithQuestion')", conditionExpression);
    }

    [Theory]
    [InlineData("")]
    [InlineData("using:Demo.Namespace")]
    [InlineData("?IsTypePresent(\"Demo.Type\")")]
    [InlineData("using:Demo.Namespace?")]
    public void TrySplit_Rejects_Invalid_Shapes(string rawNamespace)
    {
        Assert.False(XamlConditionalNamespaceUriSemantics.TrySplit(rawNamespace, out _, out _));
    }
}
