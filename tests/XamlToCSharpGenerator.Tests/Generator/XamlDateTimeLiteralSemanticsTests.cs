using System;
using System.Globalization;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlDateTimeLiteralSemanticsTests
{
    [Theory]
    [InlineData("2026-03-01T10:15:30Z")]
    [InlineData("2026-03-01T10:15:30")]
    [InlineData(" 2026-03-01T10:15:30.1234567Z ")]
    public void TryParseRoundtrip_Matches_Runtime_Roundtrip_Parse(string token)
    {
        Assert.True(XamlDateTimeLiteralSemantics.TryParseRoundtrip(token, out var value));

        var expected = DateTime.Parse(
            token.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        Assert.Equal(expected, value);
        Assert.Equal(expected.Kind, value.Kind);
    }

    [Fact]
    public void TryParseRoundtrip_Returns_False_For_Invalid_Value()
    {
        Assert.False(XamlDateTimeLiteralSemantics.TryParseRoundtrip("not-a-datetime", out _));
    }
}
