using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlConditionalNamespaceUtilitiesTests
{
    [Fact]
    public void TrySplitConditionalNamespaceUri_Parses_Valid_Conditional_Namespace()
    {
        var success = XamlConditionalNamespaceUtilities.TrySplitConditionalNamespaceUri(
            "using:Demo.Namespace?IsTypePresent(\"Demo.Type\")",
            out var normalizedNamespace,
            out var conditionExpression);

        Assert.True(success);
        Assert.Equal("using:Demo.Namespace", normalizedNamespace);
        Assert.Equal("IsTypePresent(\"Demo.Type\")", conditionExpression);
    }

    [Fact]
    public void TrySplitConditionalNamespaceUri_Rejects_Invalid_Condition_Shape()
    {
        var success = XamlConditionalNamespaceUtilities.TrySplitConditionalNamespaceUri(
            "using:Demo.Namespace?WindowsOnly",
            out _,
            out _);

        Assert.False(success);
    }

    [Fact]
    public void NormalizeXmlNamespace_Strips_Conditional_Suffix_When_Valid()
    {
        var normalized = XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(
            "using:Demo.Namespace?IsApiContractPresent(\"Windows.Foundation.UniversalApiContract\", 10)");

        Assert.Equal("using:Demo.Namespace", normalized);
    }
}
