using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class StaticResourceReferenceParserTests
{
    [Theory]
    [InlineData("{StaticResource Theme.Base}", "Theme.Base")]
    [InlineData("{DynamicResource Theme.Base}", "Theme.Base")]
    [InlineData("{StaticResource ResourceKey=Theme.Base}", "Theme.Base")]
    [InlineData("{StaticResource Key='Theme.Base'}", "Theme.Base")]
    [InlineData("Theme.Base", "Theme.Base")]
    [InlineData("'Theme.Base'", "Theme.Base")]
    public void TryExtractResourceKey_Returns_Expected_Key(string expression, string expectedKey)
    {
        var resolved = StaticResourceReferenceParser.TryExtractResourceKey(
            expression,
            out var key);

        Assert.True(resolved);
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void TryExtractResourceKey_Rejects_Non_Resource_Markup()
    {
        var resolved = StaticResourceReferenceParser.TryExtractResourceKey(
            "{Binding Path=Name}",
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryExtractResourceKey_Preserves_Nested_XType_Key()
    {
        var resolved = StaticResourceReferenceParser.TryExtractResourceKey(
            "{StaticResource {x:Type local:Button}}",
            out var key);

        Assert.True(resolved);
        Assert.Equal("{x:Type local:Button}", key);
    }
}
