using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlAvaloniaKeyGestureLiteralSemanticsTests
{
    [Theory]
    [InlineData("Ctrl+Shift+A", "A", "Control", "Shift")]
    [InlineData("cmd+.", "OemPeriod", "Meta")]
    [InlineData("Ctrl++", "OemPlus", "Control")]
    [InlineData("Alt", null, "Alt")]
    [InlineData("Shift+Win+F10", "F10", "Shift", "Meta")]
    public void TryParse_Parses_Canonical_KeyGesture_Literals(
        string literal,
        string? expectedKeyToken,
        params string[] expectedModifiers)
    {
        var ok = XamlAvaloniaKeyGestureLiteralSemantics.TryParse(
            literal,
            out var keyToken,
            out var modifiers);

        Assert.True(ok);
        Assert.Equal(expectedKeyToken, keyToken);
        Assert.Equal(expectedModifiers, modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+??")]
    [InlineData("Ctrl+Shift+")]
    public void TryParse_Rejects_Invalid_Or_Unsupported_Literals(string literal)
    {
        var ok = XamlAvaloniaKeyGestureLiteralSemantics.TryParse(
            literal,
            out _,
            out _);

        Assert.False(ok);
    }
}
