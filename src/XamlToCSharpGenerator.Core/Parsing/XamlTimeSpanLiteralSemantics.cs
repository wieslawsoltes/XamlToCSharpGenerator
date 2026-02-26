using System;
using System.Globalization;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlTimeSpanLiteralSemantics
{
    public static bool TryParse(string? value, out TimeSpan parsedTimeSpan)
    {
        parsedTimeSpan = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out parsedTimeSpan))
        {
            return true;
        }

        if (!trimmed.Contains(':', StringComparison.Ordinal) &&
            double.TryParse(
                trimmed,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            parsedTimeSpan = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return false;
    }
}
