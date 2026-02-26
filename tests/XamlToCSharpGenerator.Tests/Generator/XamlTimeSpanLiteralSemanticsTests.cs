using System;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlTimeSpanLiteralSemanticsTests
{
    [Theory]
    [InlineData("00:00:05", 5)]
    [InlineData(" 2 ", 2)]
    public void TryParse_Preserves_TimeSpan_Parse_Behavior(string token, int expectedSecondsOrDays)
    {
        Assert.True(XamlTimeSpanLiteralSemantics.TryParse(token, out var value));
        if (token.Trim() == "2")
        {
            Assert.Equal(TimeSpan.FromDays(expectedSecondsOrDays), value);
            return;
        }

        Assert.Equal(TimeSpan.FromSeconds(expectedSecondsOrDays), value);
    }

    [Fact]
    public void TryParse_Uses_Numeric_Seconds_Fallback_When_TimeSpan_Parse_Fails()
    {
        Assert.True(XamlTimeSpanLiteralSemantics.TryParse("1e2", out var value));
        Assert.Equal(TimeSpan.FromSeconds(100), value);
    }

    [Fact]
    public void TryParse_Returns_False_For_Invalid_Value()
    {
        Assert.False(XamlTimeSpanLiteralSemantics.TryParse("not-a-timespan", out _));
    }
}
