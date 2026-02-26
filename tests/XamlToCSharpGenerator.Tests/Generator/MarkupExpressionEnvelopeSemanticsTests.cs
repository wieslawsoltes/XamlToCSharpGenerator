using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class MarkupExpressionEnvelopeSemanticsTests
{
    [Fact]
    public void TryExtractInnerContent_Parses_Valid_Markup_Envelope()
    {
        Assert.True(MarkupExpressionEnvelopeSemantics.TryExtractInnerContent("{Binding Name}", out var inner));
        Assert.Equal("Binding Name", inner);
    }

    [Fact]
    public void TryExtractInnerContent_Rejects_NonMarkup_Text()
    {
        Assert.False(MarkupExpressionEnvelopeSemantics.TryExtractInnerContent("Binding Name", out _));
        Assert.False(MarkupExpressionEnvelopeSemantics.TryExtractInnerContent("{ }", out _));
        Assert.False(MarkupExpressionEnvelopeSemantics.TryExtractInnerContent("   ", out _));
    }
}
