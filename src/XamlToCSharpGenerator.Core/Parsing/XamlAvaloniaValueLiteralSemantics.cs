using System;
using System.Globalization;

namespace XamlToCSharpGenerator.Core.Parsing;

public enum AvaloniaGridLengthLiteralUnit
{
    Pixel,
    Star,
    Auto
}

public enum AvaloniaRelativeUnitLiteral
{
    Absolute,
    Relative
}

public static class XamlAvaloniaValueLiteralSemantics
{
    private const NumberStyles FloatingNumberStyles = NumberStyles.Float | NumberStyles.AllowThousands;
    private const NumberStyles IntegerNumberStyles = NumberStyles.Integer;

    public static bool TryParseThickness(
        string? value,
        out int componentCount,
        out double left,
        out double top,
        out double right,
        out double bottom)
    {
        componentCount = 0;
        left = 0d;
        top = 0d;
        right = 0d;
        bottom = 0d;

        if (!TryParseDoubles(value, 1, 4, out componentCount, out var values) ||
            componentCount == 3)
        {
            return false;
        }

        if (componentCount == 1)
        {
            left = values[0];
            top = values[0];
            right = values[0];
            bottom = values[0];
            return true;
        }

        if (componentCount == 2)
        {
            left = values[0];
            top = values[1];
            right = values[0];
            bottom = values[1];
            return true;
        }

        left = values[0];
        top = values[1];
        right = values[2];
        bottom = values[3];
        return true;
    }

    public static bool TryParseCornerRadius(
        string? value,
        out int componentCount,
        out double topLeft,
        out double topRight,
        out double bottomRight,
        out double bottomLeft)
    {
        componentCount = 0;
        topLeft = 0d;
        topRight = 0d;
        bottomRight = 0d;
        bottomLeft = 0d;

        if (!TryParseDoubles(value, 1, 4, out componentCount, out var values) ||
            componentCount == 3)
        {
            return false;
        }

        if (componentCount == 1)
        {
            topLeft = values[0];
            topRight = values[0];
            bottomRight = values[0];
            bottomLeft = values[0];
            return true;
        }

        if (componentCount == 2)
        {
            topLeft = values[0];
            topRight = values[1];
            bottomRight = values[0];
            bottomLeft = values[1];
            return true;
        }

        topLeft = values[0];
        topRight = values[1];
        bottomRight = values[2];
        bottomLeft = values[3];
        return true;
    }

    public static bool TryParsePoint(string? value, out double x, out double y)
    {
        x = 0d;
        y = 0d;
        if (!TryParseDoubles(value, 2, 2, out var count, out var values) || count != 2)
        {
            return false;
        }

        x = values[0];
        y = values[1];
        return true;
    }

    public static bool TryParseVector(string? value, out double x, out double y)
    {
        return TryParsePoint(value, out x, out y);
    }

    public static bool TryParseSize(string? value, out double width, out double height)
    {
        return TryParsePoint(value, out width, out height);
    }

    public static bool TryParseRect(
        string? value,
        out double x,
        out double y,
        out double width,
        out double height)
    {
        x = 0d;
        y = 0d;
        width = 0d;
        height = 0d;
        if (!TryParseDoubles(value, 4, 4, out var count, out var values) || count != 4)
        {
            return false;
        }

        x = values[0];
        y = values[1];
        width = values[2];
        height = values[3];
        return true;
    }

    public static bool TryParseVector3D(
        string? value,
        out double x,
        out double y,
        out double z)
    {
        x = 0d;
        y = 0d;
        z = 0d;
        if (!TryParseDoubles(value, 3, 3, out var count, out var values) || count != 3)
        {
            return false;
        }

        x = values[0];
        y = values[1];
        z = values[2];
        return true;
    }

    public static bool TryParseMatrix(
        string? value,
        out int componentCount,
        out double m11,
        out double m12,
        out double m21,
        out double m22,
        out double m31,
        out double m32,
        out double m13,
        out double m23,
        out double m33)
    {
        componentCount = 0;
        m11 = 0d;
        m12 = 0d;
        m21 = 0d;
        m22 = 0d;
        m31 = 0d;
        m32 = 0d;
        m13 = 0d;
        m23 = 0d;
        m33 = 0d;

        if (!TryParseDoubles(value, 6, 9, out componentCount, out var values) ||
            (componentCount != 6 && componentCount != 9))
        {
            return false;
        }

        m11 = values[0];
        m12 = values[1];
        m21 = values[2];
        m22 = values[3];
        m31 = values[4];
        m32 = values[5];

        if (componentCount == 9)
        {
            m13 = values[6];
            m23 = values[7];
            m33 = values[8];
        }

        return true;
    }

    public static bool TryParsePixelPoint(string? value, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!TryParseIntegers(value, 2, 2, out var count, out var values) || count != 2)
        {
            return false;
        }

        x = values[0];
        y = values[1];
        return true;
    }

    public static bool TryParsePixelSize(string? value, out int width, out int height)
    {
        return TryParsePixelPoint(value, out width, out height);
    }

    public static bool TryParsePixelRect(
        string? value,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        if (!TryParseIntegers(value, 4, 4, out var count, out var values) || count != 4)
        {
            return false;
        }

        x = values[0];
        y = values[1];
        width = values[2];
        height = values[3];
        return true;
    }

    public static bool TryParseGridLength(
        string? value,
        out AvaloniaGridLengthLiteralUnit unit,
        out double numericValue)
    {
        unit = AvaloniaGridLengthLiteralUnit.Pixel;
        numericValue = 0d;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var source = value;
        if (source is null)
        {
            return false;
        }

        var trimmed = source.Trim();
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            unit = AvaloniaGridLengthLiteralUnit.Auto;
            numericValue = 1d;
            return true;
        }

        if (trimmed.EndsWith("*", StringComparison.Ordinal))
        {
            var numericToken = trimmed.Substring(0, trimmed.Length - 1).Trim();
            if (numericToken.Length == 0)
            {
                unit = AvaloniaGridLengthLiteralUnit.Star;
                numericValue = 1d;
                return true;
            }

            if (!double.TryParse(numericToken, FloatingNumberStyles, CultureInfo.InvariantCulture, out numericValue))
            {
                return false;
            }

            unit = AvaloniaGridLengthLiteralUnit.Star;
            return true;
        }

        if (!double.TryParse(trimmed, FloatingNumberStyles, CultureInfo.InvariantCulture, out numericValue))
        {
            return false;
        }

        unit = AvaloniaGridLengthLiteralUnit.Pixel;
        return true;
    }

    public static bool TryParseRelativePoint(
        string? value,
        out double x,
        out double y,
        out AvaloniaRelativeUnitLiteral unit)
    {
        x = 0d;
        y = 0d;
        unit = AvaloniaRelativeUnitLiteral.Absolute;

        if (!TryReadTokens(value, 2, 2, out var count, out var tokens) || count != 2)
        {
            return false;
        }

        var xToken = tokens[0];
        var yToken = tokens[1];
        var xIsRelative = xToken.EndsWith("%", StringComparison.Ordinal);
        var yIsRelative = yToken.EndsWith("%", StringComparison.Ordinal);
        if (xIsRelative != yIsRelative)
        {
            return false;
        }

        var scale = 1d;
        if (xIsRelative)
        {
            xToken = xToken.Substring(0, xToken.Length - 1);
            yToken = yToken.Substring(0, yToken.Length - 1);
            unit = AvaloniaRelativeUnitLiteral.Relative;
            scale = 0.01d;
        }

        if (!double.TryParse(xToken, FloatingNumberStyles, CultureInfo.InvariantCulture, out var parsedX) ||
            !double.TryParse(yToken, FloatingNumberStyles, CultureInfo.InvariantCulture, out var parsedY))
        {
            return false;
        }

        x = parsedX * scale;
        y = parsedY * scale;
        return true;
    }

    public static bool TryParseRelativeScalar(
        string? value,
        out double scalar,
        out AvaloniaRelativeUnitLiteral unit)
    {
        scalar = 0d;
        unit = AvaloniaRelativeUnitLiteral.Absolute;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var source = value;
        if (source is null)
        {
            return false;
        }

        var trimmed = source.Trim();
        var scale = 1d;
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
            unit = AvaloniaRelativeUnitLiteral.Relative;
            scale = 0.01d;
        }

        if (!double.TryParse(trimmed, FloatingNumberStyles, CultureInfo.InvariantCulture, out var parsedScalar))
        {
            return false;
        }

        scalar = parsedScalar * scale;
        return true;
    }

    public static bool TryParseRelativeRect(
        string? value,
        out double x,
        out double y,
        out double width,
        out double height,
        out AvaloniaRelativeUnitLiteral unit)
    {
        x = 0d;
        y = 0d;
        width = 0d;
        height = 0d;
        unit = AvaloniaRelativeUnitLiteral.Absolute;

        if (!TryReadTokens(value, 4, 4, out var count, out var tokens) || count != 4)
        {
            return false;
        }

        var relativeFlags = new bool[4];
        for (var index = 0; index < 4; index++)
        {
            relativeFlags[index] = tokens[index].EndsWith("%", StringComparison.Ordinal);
        }

        var allRelative = relativeFlags[0] && relativeFlags[1] && relativeFlags[2] && relativeFlags[3];
        var noneRelative = !relativeFlags[0] && !relativeFlags[1] && !relativeFlags[2] && !relativeFlags[3];
        if (!allRelative && !noneRelative)
        {
            return false;
        }

        var scale = 1d;
        if (allRelative)
        {
            unit = AvaloniaRelativeUnitLiteral.Relative;
            scale = 0.01d;
        }

        var parsed = new double[4];
        for (var index = 0; index < 4; index++)
        {
            var token = tokens[index];
            if (allRelative)
            {
                token = token.Substring(0, token.Length - 1);
            }

            if (!double.TryParse(token, FloatingNumberStyles, CultureInfo.InvariantCulture, out parsed[index]))
            {
                return false;
            }
        }

        x = parsed[0] * scale;
        y = parsed[1] * scale;
        width = parsed[2] * scale;
        height = parsed[3] * scale;
        return true;
    }

    public static bool TryParseHexColor(string? value, out uint argb)
    {
        argb = 0u;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var source = value;
        if (source is null)
        {
            return false;
        }

        var trimmed = source.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '#')
        {
            return false;
        }

        var input = trimmed.Substring(1);
        byte a;
        byte r;
        byte g;
        byte b;

        if (input.Length == 3)
        {
            if (!TryParseHexNibble(input[0], out var rNibble) ||
                !TryParseHexNibble(input[1], out var gNibble) ||
                !TryParseHexNibble(input[2], out var bNibble))
            {
                return false;
            }

            a = 0xFF;
            r = (byte)((rNibble << 4) | rNibble);
            g = (byte)((gNibble << 4) | gNibble);
            b = (byte)((bNibble << 4) | bNibble);
        }
        else if (input.Length == 4)
        {
            if (!TryParseHexNibble(input[0], out var aNibble) ||
                !TryParseHexNibble(input[1], out var rNibble) ||
                !TryParseHexNibble(input[2], out var gNibble) ||
                !TryParseHexNibble(input[3], out var bNibble))
            {
                return false;
            }

            a = (byte)((aNibble << 4) | aNibble);
            r = (byte)((rNibble << 4) | rNibble);
            g = (byte)((gNibble << 4) | gNibble);
            b = (byte)((bNibble << 4) | bNibble);
        }
        else if (input.Length == 6)
        {
            if (!TryParseHexByte(input, 0, out r) ||
                !TryParseHexByte(input, 2, out g) ||
                !TryParseHexByte(input, 4, out b))
            {
                return false;
            }

            a = 0xFF;
        }
        else if (input.Length == 8)
        {
            if (!TryParseHexByte(input, 0, out a) ||
                !TryParseHexByte(input, 2, out r) ||
                !TryParseHexByte(input, 4, out g) ||
                !TryParseHexByte(input, 6, out b))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        argb = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        return true;
    }

    private static bool TryParseDoubles(
        string? value,
        int minCount,
        int maxCount,
        out int count,
        out double[] values)
    {
        count = 0;
        values = new double[maxCount];

        if (!TryReadTokens(value, minCount, maxCount, out count, out var tokens))
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            if (!double.TryParse(tokens[index], FloatingNumberStyles, CultureInfo.InvariantCulture, out values[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseIntegers(
        string? value,
        int minCount,
        int maxCount,
        out int count,
        out int[] values)
    {
        count = 0;
        values = new int[maxCount];

        if (!TryReadTokens(value, minCount, maxCount, out count, out var tokens))
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            if (!int.TryParse(tokens[index], IntegerNumberStyles, CultureInfo.InvariantCulture, out values[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadTokens(
        string? value,
        int minCount,
        int maxCount,
        out int count,
        out string[] tokens)
    {
        count = 0;
        tokens = new string[maxCount];
        for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            tokens[tokenIndex] = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var source = value;
        if (source is null)
        {
            return false;
        }

        var text = source.Trim();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && IsValueSeparator(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var tokenStart = index;
            while (index < text.Length && !IsValueSeparator(text[index]))
            {
                index++;
            }

            if (count >= maxCount)
            {
                return false;
            }

            var tokenLength = index - tokenStart;
            if (tokenLength <= 0)
            {
                continue;
            }

            tokens[count] = text.Substring(tokenStart, tokenLength);
            count++;
        }

        return count >= minCount;
    }

    private static bool IsValueSeparator(char token)
    {
        return token == ',' || char.IsWhiteSpace(token);
    }

    private static bool TryParseHexByte(string input, int index, out byte value)
    {
        value = 0;
        if (index < 0 || index + 1 >= input.Length)
        {
            return false;
        }

        if (!TryParseHexNibble(input[index], out var high) ||
            !TryParseHexNibble(input[index + 1], out var low))
        {
            return false;
        }

        value = (byte)((high << 4) | low);
        return true;
    }

    private static bool TryParseHexNibble(char token, out int value)
    {
        if (token >= '0' && token <= '9')
        {
            value = token - '0';
            return true;
        }

        if (token >= 'A' && token <= 'F')
        {
            value = token - 'A' + 10;
            return true;
        }

        if (token >= 'a' && token <= 'f')
        {
            value = token - 'a' + 10;
            return true;
        }

        value = 0;
        return false;
    }
}
