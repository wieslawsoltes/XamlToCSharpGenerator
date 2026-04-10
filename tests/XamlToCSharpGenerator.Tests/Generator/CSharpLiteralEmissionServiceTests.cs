using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CSharpLiteralEmissionServiceTests
{
    [Fact]
    public void EscapeStringLiteral_Escapes_Quotes_Backslashes_And_Newlines()
    {
        var service = new CSharpLiteralEmissionService();

        var escaped = service.EscapeStringLiteral("a\\b\"c\r\nd");

        Assert.Equal("a\\\\b\\\"c\\nd", escaped);
    }

    [Fact]
    public void QuoteOrNull_Produces_Null_For_Whitespace_And_Quoted_String_Otherwise()
    {
        var service = new CSharpLiteralEmissionService();

        Assert.Equal("null", service.QuoteOrNull(" "));
        Assert.Equal("\"abc\\nvalue\"", service.QuoteOrNull("abc\nvalue"));
    }

    [Fact]
    public void BoolLiteral_And_NormalizeCommentText_Are_Deterministic()
    {
        var service = new CSharpLiteralEmissionService();

        Assert.Equal("true", service.BoolLiteral(true));
        Assert.Equal("false", service.BoolLiteral(false));
        Assert.Equal("line1  line2", service.NormalizeCommentText("line1\r\nline2"));
    }
}
