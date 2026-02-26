using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlRuntimeBindingPathSemanticsTests
{
    [Theory]
    [InlineData("(vm:Person).Name", "Name")]
    [InlineData("(global::Demo.ViewModels.Person).Address.City", "Address.City")]
    [InlineData("(global::Demo.ViewModels.Person).", ".")]
    [InlineData(" Name ", "Name")]
    [InlineData("(Person).Name", "(Person).Name")]
    [InlineData("(vm:List[0]).Name", "(vm:List[0]).Name")]
    public void NormalizePath_Normalizes_Runtime_TypeCast_Prefix(string input, string expected)
    {
        Assert.Equal(expected, XamlRuntimeBindingPathSemantics.NormalizePath(input));
    }

    [Fact]
    public void TrySplitTypeCastPrefix_Splits_Valid_Prefix()
    {
        Assert.True(XamlRuntimeBindingPathSemantics.TrySplitTypeCastPrefix("(vm:Type).Member", out var typeToken, out var remainder));
        Assert.Equal("vm:Type", typeToken);
        Assert.Equal("Member", remainder);
    }

    [Theory]
    [InlineData("vm:Type", true)]
    [InlineData("global::Demo.Type", true)]
    [InlineData("Demo.Type", false)]
    [InlineData("vm:Type[]", false)]
    [InlineData("vm:Type WithSpace", false)]
    public void IsTypeCastToken_Validates_Runtime_TypeCast_Token(string token, bool expected)
    {
        Assert.Equal(expected, XamlRuntimeBindingPathSemantics.IsTypeCastToken(token));
    }
}
