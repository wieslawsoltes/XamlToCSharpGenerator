using System;
using System.Collections.Generic;
using System.Globalization;

namespace XamlToCSharpGenerator.Core.Parsing;

public enum AvaloniaTransformOperationLiteralKind
{
    Translate,
    Scale,
    Skew,
    Rotate,
    Matrix
}

public readonly struct AvaloniaTransformOperationLiteral
{
    public AvaloniaTransformOperationLiteral(
        AvaloniaTransformOperationLiteralKind kind,
        double value1,
        double value2 = 0d,
        double value3 = 0d,
        double value4 = 0d,
        double value5 = 0d,
        double value6 = 0d)
    {
        Kind = kind;
        Value1 = value1;
        Value2 = value2;
        Value3 = value3;
        Value4 = value4;
        Value5 = value5;
        Value6 = value6;
    }

    public AvaloniaTransformOperationLiteralKind Kind { get; }

    public double Value1 { get; }

    public double Value2 { get; }

    public double Value3 { get; }

    public double Value4 { get; }

    public double Value5 { get; }

    public double Value6 { get; }
}

public static class XamlAvaloniaTransformLiteralSemantics
{
    private enum Unit
    {
        None,
        Pixel,
        Radian,
        Gradian,
        Degree,
        Turn
    }

    private enum TransformFunction
    {
        Invalid,
        Translate,
        TranslateX,
        TranslateY,
        Scale,
        ScaleX,
        ScaleY,
        Skew,
        SkewX,
        SkewY,
        Rotate,
        Matrix
    }

    private readonly struct UnitValue
    {
        public UnitValue(Unit unit, double value)
        {
            Unit = unit;
            Value = value;
        }

        public Unit Unit { get; }

        public double Value { get; }
    }

    public static bool TryParse(
        string? value,
        out bool isIdentity,
        out AvaloniaTransformOperationLiteral[] operations)
    {
        isIdentity = false;
        operations = Array.Empty<AvaloniaTransformOperationLiteral>();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var source = value;
        if (source is null)
        {
            return false;
        }

        var span = source.AsSpan().Trim();
        if (SpanEquals(span, "none"))
        {
            isIdentity = true;
            return true;
        }

        var parsedOperations = new List<AvaloniaTransformOperationLiteral>(4);
        while (true)
        {
            var beginIndex = span.IndexOf('(');
            var endIndex = span.IndexOf(')');
            if (beginIndex < 0 || endIndex < 0 || endIndex <= beginIndex)
            {
                return false;
            }

            var namePart = span.Slice(0, beginIndex).Trim();
            if (!TryParseFunction(namePart, out var function))
            {
                return false;
            }

            var valuePart = span.Slice(beginIndex + 1, endIndex - beginIndex - 1).Trim();
            if (!TryParseFunctionValues(function, valuePart, out var operation))
            {
                return false;
            }

            parsedOperations.Add(operation);
            span = span.Slice(endIndex + 1);
            if (IsAllWhiteSpace(span))
            {
                break;
            }
        }

        if (parsedOperations.Count == 0)
        {
            return false;
        }

        operations = parsedOperations.ToArray();
        return true;
    }

    private static bool TryParseFunction(ReadOnlySpan<char> token, out TransformFunction function)
    {
        function = TransformFunction.Invalid;
        if (token.Length == 0)
        {
            return false;
        }

        if (SpanEquals(token, "translate"))
        {
            function = TransformFunction.Translate;
            return true;
        }

        if (SpanEquals(token, "translateX"))
        {
            function = TransformFunction.TranslateX;
            return true;
        }

        if (SpanEquals(token, "translateY"))
        {
            function = TransformFunction.TranslateY;
            return true;
        }

        if (SpanEquals(token, "scale"))
        {
            function = TransformFunction.Scale;
            return true;
        }

        if (SpanEquals(token, "scaleX"))
        {
            function = TransformFunction.ScaleX;
            return true;
        }

        if (SpanEquals(token, "scaleY"))
        {
            function = TransformFunction.ScaleY;
            return true;
        }

        if (SpanEquals(token, "skew"))
        {
            function = TransformFunction.Skew;
            return true;
        }

        if (SpanEquals(token, "skewX"))
        {
            function = TransformFunction.SkewX;
            return true;
        }

        if (SpanEquals(token, "skewY"))
        {
            function = TransformFunction.SkewY;
            return true;
        }

        if (SpanEquals(token, "rotate"))
        {
            function = TransformFunction.Rotate;
            return true;
        }

        if (SpanEquals(token, "matrix"))
        {
            function = TransformFunction.Matrix;
            return true;
        }

        return false;
    }

    private static bool TryParseFunctionValues(
        TransformFunction function,
        ReadOnlySpan<char> functionPart,
        out AvaloniaTransformOperationLiteral operation)
    {
        operation = default;
        switch (function)
        {
            case TransformFunction.Scale:
            case TransformFunction.ScaleX:
            case TransformFunction.ScaleY:
            {
                var scaleX = new UnitValue(Unit.None, 1d);
                var scaleY = new UnitValue(Unit.None, 1d);
                if (!TryParseValuePair(functionPart, out scaleX, out scaleY, out var count))
                {
                    return false;
                }

                if (count != 1 &&
                    (function == TransformFunction.ScaleX || function == TransformFunction.ScaleY))
                {
                    return false;
                }

                if (!VerifyZeroOrUnit(scaleX, Unit.None) || !VerifyZeroOrUnit(scaleY, Unit.None))
                {
                    return false;
                }

                if (function == TransformFunction.ScaleY)
                {
                    scaleY = scaleX;
                    scaleX = new UnitValue(Unit.None, 1d);
                }
                else if (function == TransformFunction.ScaleX)
                {
                    scaleY = new UnitValue(Unit.None, 1d);
                }
                else if (function == TransformFunction.Scale && count == 1)
                {
                    scaleY = scaleX;
                }

                operation = new AvaloniaTransformOperationLiteral(
                    AvaloniaTransformOperationLiteralKind.Scale,
                    scaleX.Value,
                    scaleY.Value);
                return true;
            }
            case TransformFunction.Skew:
            case TransformFunction.SkewX:
            case TransformFunction.SkewY:
            {
                var skewX = new UnitValue(Unit.None, 0d);
                var skewY = new UnitValue(Unit.None, 0d);
                if (!TryParseValuePair(functionPart, out skewX, out skewY, out var count))
                {
                    return false;
                }

                if (count != 1 &&
                    (function == TransformFunction.SkewX || function == TransformFunction.SkewY))
                {
                    return false;
                }

                if (!VerifyZeroOrAngle(skewX) || !VerifyZeroOrAngle(skewY))
                {
                    return false;
                }

                if (function == TransformFunction.SkewY)
                {
                    skewY = skewX;
                    skewX = new UnitValue(Unit.None, 0d);
                }

                operation = new AvaloniaTransformOperationLiteral(
                    AvaloniaTransformOperationLiteralKind.Skew,
                    ToRadians(skewX),
                    ToRadians(skewY));
                return true;
            }
            case TransformFunction.Rotate:
            {
                if (!TryParseValuePair(functionPart, out var angle, out _, out var count) || count != 1)
                {
                    return false;
                }

                if (!VerifyZeroOrAngle(angle))
                {
                    return false;
                }

                operation = new AvaloniaTransformOperationLiteral(
                    AvaloniaTransformOperationLiteralKind.Rotate,
                    ToRadians(angle));
                return true;
            }
            case TransformFunction.Translate:
            case TransformFunction.TranslateX:
            case TransformFunction.TranslateY:
            {
                var translateX = new UnitValue(Unit.None, 0d);
                var translateY = new UnitValue(Unit.None, 0d);
                if (!TryParseValuePair(functionPart, out translateX, out translateY, out var count))
                {
                    return false;
                }

                if (count != 1 &&
                    (function == TransformFunction.TranslateX || function == TransformFunction.TranslateY))
                {
                    return false;
                }

                if (!VerifyZeroOrUnit(translateX, Unit.Pixel) ||
                    !VerifyZeroOrUnit(translateY, Unit.Pixel))
                {
                    return false;
                }

                if (function == TransformFunction.TranslateY)
                {
                    translateY = translateX;
                    translateX = new UnitValue(Unit.None, 0d);
                }

                operation = new AvaloniaTransformOperationLiteral(
                    AvaloniaTransformOperationLiteralKind.Translate,
                    translateX.Value,
                    translateY.Value);
                return true;
            }
            case TransformFunction.Matrix:
            {
                var values = new UnitValue[6];
                if (!TryParseCommaDelimitedValues(functionPart, values, out var count) || count != 6)
                {
                    return false;
                }

                for (var index = 0; index < values.Length; index++)
                {
                    if (!VerifyZeroOrUnit(values[index], Unit.None))
                    {
                        return false;
                    }
                }

                operation = new AvaloniaTransformOperationLiteral(
                    AvaloniaTransformOperationLiteralKind.Matrix,
                    values[0].Value,
                    values[1].Value,
                    values[2].Value,
                    values[3].Value,
                    values[4].Value,
                    values[5].Value);
                return true;
            }
            default:
                return false;
        }
    }

    private static bool TryParseValuePair(
        ReadOnlySpan<char> part,
        out UnitValue leftValue,
        out UnitValue rightValue,
        out int count)
    {
        leftValue = default;
        rightValue = default;
        count = 0;

        var commaIndex = part.IndexOf(',');
        if (commaIndex >= 0)
        {
            var leftPart = part.Slice(0, commaIndex).Trim();
            var rightPart = part.Slice(commaIndex + 1).Trim();
            if (leftPart.Length == 0 || rightPart.Length == 0)
            {
                return false;
            }

            if (!TryParseUnitValue(leftPart, out leftValue) || !TryParseUnitValue(rightPart, out rightValue))
            {
                return false;
            }

            count = 2;
            return true;
        }

        if (!TryParseUnitValue(part.Trim(), out leftValue))
        {
            return false;
        }

        count = 1;
        return true;
    }

    private static bool TryParseCommaDelimitedValues(
        ReadOnlySpan<char> part,
        UnitValue[] outputValues,
        out int count)
    {
        count = 0;

        while (true)
        {
            var commaIndex = part.IndexOf(',');
            if (commaIndex < 0)
            {
                var trailing = part.Trim();
                if (trailing.Length > 0)
                {
                    if (count >= outputValues.Length || !TryParseUnitValue(trailing, out var parsedTrailing))
                    {
                        return false;
                    }

                    outputValues[count++] = parsedTrailing;
                }

                return true;
            }

            var token = part.Slice(0, commaIndex).Trim();
            if (token.Length == 0)
            {
                return false;
            }

            if (count >= outputValues.Length || !TryParseUnitValue(token, out var parsedValue))
            {
                return false;
            }

            outputValues[count++] = parsedValue;
            part = part.Slice(commaIndex + 1);
        }
    }

    private static bool TryParseUnitValue(ReadOnlySpan<char> token, out UnitValue value)
    {
        value = default;
        if (token.Length == 0)
        {
            return false;
        }

        var unitStartIndex = -1;
        for (var index = 0; index < token.Length; index++)
        {
            var current = token[index];
            if (char.IsDigit(current) || current == '-' || current == '.')
            {
                continue;
            }

            unitStartIndex = index;
            break;
        }

        Unit unit = Unit.None;
        var numberToken = token;
        if (unitStartIndex >= 0)
        {
            var unitToken = token.Slice(unitStartIndex);
            if (!TryParseUnit(unitToken, out unit))
            {
                return false;
            }

            numberToken = token.Slice(0, unitStartIndex);
        }

        if (numberToken.Length == 0 ||
            !double.TryParse(numberToken.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
        {
            return false;
        }

        value = new UnitValue(unit, parsedNumber);
        return true;
    }

    private static bool TryParseUnit(ReadOnlySpan<char> token, out Unit unit)
    {
        if (SpanEquals(token, "deg"))
        {
            unit = Unit.Degree;
            return true;
        }

        if (SpanEquals(token, "grad"))
        {
            unit = Unit.Gradian;
            return true;
        }

        if (SpanEquals(token, "rad"))
        {
            unit = Unit.Radian;
            return true;
        }

        if (SpanEquals(token, "turn"))
        {
            unit = Unit.Turn;
            return true;
        }

        if (SpanEquals(token, "px"))
        {
            unit = Unit.Pixel;
            return true;
        }

        unit = Unit.None;
        return false;
    }

    private static bool VerifyZeroOrUnit(UnitValue value, Unit unit)
    {
        var isZero = value.Unit == Unit.None && value.Value == 0d;
        return isZero || value.Unit == unit;
    }

    private static bool VerifyZeroOrAngle(UnitValue value)
    {
        return value.Value == 0d || IsAngleUnit(value.Unit);
    }

    private static bool IsAngleUnit(Unit unit)
    {
        switch (unit)
        {
            case Unit.Radian:
            case Unit.Gradian:
            case Unit.Degree:
            case Unit.Turn:
                return true;
            default:
                return false;
        }
    }

    private static double ToRadians(UnitValue value)
    {
        switch (value.Unit)
        {
            case Unit.Radian:
                return value.Value;
            case Unit.Gradian:
                return value.Value * (Math.PI / 200d);
            case Unit.Degree:
                return value.Value * (Math.PI / 180d);
            case Unit.Turn:
                return value.Value * (2d * Math.PI);
            default:
                return value.Value;
        }
    }

    private static bool SpanEquals(ReadOnlySpan<char> span, string value)
    {
        if (span.Length != value.Length)
        {
            return false;
        }

        for (var index = 0; index < span.Length; index++)
        {
            if (char.ToUpperInvariant(span[index]) != char.ToUpperInvariant(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllWhiteSpace(ReadOnlySpan<char> span)
    {
        for (var index = 0; index < span.Length; index++)
        {
            if (!char.IsWhiteSpace(span[index]))
            {
                return false;
            }
        }

        return true;
    }
}
