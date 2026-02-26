using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlQuotedValueSemanticsTests
{
    [Fact]
    public void TrimAndUnquote_Removes_Outer_Whitespace_And_Wrapping_Quotes()
    {
        Assert.Equal("value", XamlQuotedValueSemantics.TrimAndUnquote("  \"value\"  "));
        Assert.Equal("value", XamlQuotedValueSemantics.TrimAndUnquote("  'value'  "));
    }

    [Fact]
    public void UnquoteWrapped_Removes_Only_Matching_Outer_Quotes()
    {
        Assert.Equal("alpha", XamlQuotedValueSemantics.UnquoteWrapped("\"alpha\""));
        Assert.Equal("beta", XamlQuotedValueSemantics.UnquoteWrapped("'beta'"));
        Assert.Equal("gamma", XamlQuotedValueSemantics.UnquoteWrapped("gamma"));
    }
}
