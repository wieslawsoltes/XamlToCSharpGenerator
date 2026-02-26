using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class EventBindingSourceModeSemanticsTests
{
    [Theory]
    [InlineData("DataContextThenRoot", ResolvedEventBindingSourceMode.DataContextThenRoot)]
    [InlineData("Default", ResolvedEventBindingSourceMode.DataContextThenRoot)]
    [InlineData("DataContext", ResolvedEventBindingSourceMode.DataContext)]
    [InlineData("Root", ResolvedEventBindingSourceMode.Root)]
    public void TryParse_Maps_Known_Source_Tokens(string token, ResolvedEventBindingSourceMode expected)
    {
        Assert.True(EventBindingSourceModeSemantics.TryParse(token, out var mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void TryParse_Returns_False_For_Unknown_Source_Token()
    {
        Assert.False(EventBindingSourceModeSemantics.TryParse("AnythingElse", out _));
    }
}
