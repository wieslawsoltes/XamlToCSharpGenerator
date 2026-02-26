using System;
using System.Globalization;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlScalarLiteralSemantics
{
    public static bool IsNullLiteral(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               token.Trim().Equals("null", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseBoolean(string? token, out bool value)
    {
        return bool.TryParse(token?.Trim(), out value);
    }

    public static bool TryParseInt32(string? token, out int value)
    {
        return int.TryParse(token?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseInt64(string? token, out long value)
    {
        return long.TryParse(token?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseSingle(string? token, out float value)
    {
        return float.TryParse(token?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDouble(string? token, out double value)
    {
        return double.TryParse(token?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDecimal(string? token, out decimal value)
    {
        return decimal.TryParse(token?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
