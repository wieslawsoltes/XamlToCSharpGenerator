using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class IdentifierSanitizationServiceTests
{
    [Theory]
    [InlineData("", "UnnamedElement")]
    [InlineData("Header", "Header")]
    [InlineData("Header.Text", "Header_Text")]
    [InlineData("123Header", "_123Header")]
    [InlineData("a-b c", "a_b_c")]
    public void SanitizeIdentifier_Normalizes_To_Valid_CSharp_Identifier(string input, string expected)
    {
        var service = new IdentifierSanitizationService();

        var sanitized = service.SanitizeIdentifier(input);

        Assert.Equal(expected, sanitized);
    }
}
