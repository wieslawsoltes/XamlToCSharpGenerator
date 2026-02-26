using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlIdentifierSemanticsTests
{
    [Theory]
    [InlineData("Alpha", true)]
    [InlineData("_Alpha2", true)]
    [InlineData("2Alpha", false)]
    [InlineData("Alpha-Beta", false)]
    [InlineData("Alpha.Beta", false)]
    public void IsIdentifier_Returns_Expected_Result(string value, bool expected)
    {
        Assert.Equal(expected, XamlIdentifierSemantics.IsIdentifier(value));
    }

    [Fact]
    public void TryNormalizeIdentifier_Trims_And_Validates()
    {
        Assert.True(XamlIdentifierSemantics.TryNormalizeIdentifier("  SaveCommand  ", out var normalized));
        Assert.Equal("SaveCommand", normalized);
    }
}
