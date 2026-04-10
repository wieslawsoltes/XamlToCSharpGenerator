using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class SymbolConstructionSemanticsServiceTests
{
    [Theory]
    [InlineData("double", "global::System.Double")]
    [InlineData("uint", "global::System.UInt32")]
    [InlineData("int", "System.Int32")]
    [InlineData("string", "global::System.String")]
    [InlineData("object", "System.Object")]
    public void TypeNameMatches_Normalizes_CSharp_Aliases(string actualTypeName, string expectedTypeName)
    {
        Assert.True(SymbolConstructionSemanticsService.TypeNameMatches(actualTypeName, expectedTypeName));
        Assert.True(SymbolConstructionSemanticsService.TypeNameMatches(expectedTypeName, actualTypeName));
    }
}
