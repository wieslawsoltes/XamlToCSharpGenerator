using System;
using System.Globalization;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlDateTimeLiteralSemantics
{
    public static bool TryParseRoundtrip(string? value, out DateTime dateTime)
    {
        dateTime = default;
        var source = value;
        if (source is null)
        {
            return false;
        }

        var trimmed = source.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return DateTime.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out dateTime);
    }
}
