using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlReferenceNameSemanticsTests
{
    [Theory]
    [InlineData("RootPanel", "RootPanel")]
    [InlineData("  RootPanel  ", "RootPanel")]
    [InlineData("'RootPanel'", "RootPanel")]
    [InlineData("\"RootPanel\"", "RootPanel")]
    public void TryNormalizeReferenceName_Normalizes_Valid_Values(string rawValue, string expected)
    {
        Assert.True(XamlReferenceNameSemantics.TryNormalizeReferenceName(rawValue, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("' '")]
    [InlineData("Root Panel")]
    [InlineData("Root\tPanel")]
    public void TryNormalizeReferenceName_Rejects_Invalid_Values(string rawValue)
    {
        Assert.False(XamlReferenceNameSemantics.TryNormalizeReferenceName(rawValue, out _));
    }
}
