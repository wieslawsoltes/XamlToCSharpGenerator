using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class StaticResourceReferenceParserTests
{
    [Theory]
    [InlineData("{StaticResource Theme.Base}", "Theme.Base")]
    [InlineData("{DynamicResource AccentButtonBackgroundDisabled}", "AccentButtonBackgroundDisabled")]
    [InlineData("{StaticResource ResourceKey=Theme.Base}", "Theme.Base")]
    [InlineData("{DynamicResource Key='Theme.Base'}", "Theme.Base")]
    [InlineData("Theme.Base", "Theme.Base")]
    public void TryExtractResourceKey_Parses_Supported_Forms(string expression, string expected)
    {
        var result = StaticResourceReferenceParser.TryExtractResourceKey(expression, out var resourceKey);

        Assert.True(result);
        Assert.Equal(expected, resourceKey);
    }

    [Fact]
    public void TryExtractResourceKey_Returns_False_For_Unsupported_Markup()
    {
        var result = StaticResourceReferenceParser.TryExtractResourceKey("{Binding Theme.Base}", out var resourceKey);

        Assert.False(result);
        Assert.Equal(string.Empty, resourceKey);
    }
}
