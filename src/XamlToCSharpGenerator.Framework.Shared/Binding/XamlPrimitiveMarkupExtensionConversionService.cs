using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XamlPrimitiveMarkupExtensionConversionService
{
    private readonly Func<string, string> _escape;

    public XamlPrimitiveMarkupExtensionConversionService(Func<string, string> escape)
    {
        _escape = escape;
    }

    public bool TryConvert(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression)
    {
        _ = targetType;
        expression = string.Empty;

        var rawValue = markup.PositionalArguments.Length > 0
            ? markup.PositionalArguments[0]
            : string.Empty;

        switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
        {
            case XamlMarkupExtensionKind.Null:
                expression = "null";
                return true;

            case XamlMarkupExtensionKind.True:
                expression = "true";
                return true;

            case XamlMarkupExtensionKind.False:
                expression = "false";
                return true;

            case XamlMarkupExtensionKind.String:
                expression = "\"" + _escape(rawValue) + "\"";
                return true;

            case XamlMarkupExtensionKind.Char:
                if (rawValue.Length == 1)
                {
                    expression = "'" + rawValue.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
                    return true;
                }

                return false;

            case XamlMarkupExtensionKind.Byte when byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue):
                expression = "((byte)" + byteValue.ToString(CultureInfo.InvariantCulture) + ")";
                return true;

            case XamlMarkupExtensionKind.SByte when sbyte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue):
                expression = "((sbyte)" + sbyteValue.ToString(CultureInfo.InvariantCulture) + ")";
                return true;

            case XamlMarkupExtensionKind.Int16 when short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int16Value):
                expression = "((short)" + int16Value.ToString(CultureInfo.InvariantCulture) + ")";
                return true;

            case XamlMarkupExtensionKind.UInt16 when ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uint16Value):
                expression = "((ushort)" + uint16Value.ToString(CultureInfo.InvariantCulture) + ")";
                return true;

            case XamlMarkupExtensionKind.Int32 when int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int32Value):
                expression = int32Value.ToString(CultureInfo.InvariantCulture);
                return true;

            case XamlMarkupExtensionKind.UInt32 when uint.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uint32Value):
                expression = uint32Value.ToString(CultureInfo.InvariantCulture) + "u";
                return true;

            case XamlMarkupExtensionKind.Int64 when long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int64Value):
                expression = int64Value.ToString(CultureInfo.InvariantCulture) + "L";
                return true;

            case XamlMarkupExtensionKind.UInt64 when ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uint64Value):
                expression = uint64Value.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;

            case XamlMarkupExtensionKind.Single when float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var singleValue):
                expression = FormatSingleLiteral(singleValue);
                return true;

            case XamlMarkupExtensionKind.Double when double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue):
                expression = FormatDoubleLiteral(doubleValue);
                return true;

            case XamlMarkupExtensionKind.Decimal when decimal.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var decimalValue):
                expression = decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
                return true;

            case XamlMarkupExtensionKind.Uri:
                expression = "new global::System.Uri(\"" +
                             _escape(rawValue) +
                             "\", global::System.UriKind.RelativeOrAbsolute)";
                return true;

            default:
                break;
        }

        if (XamlTimeSpanLiteralSemantics.TryParse(rawValue, out var timeSpan))
        {
            expression = "global::System.TimeSpan.FromTicks(" + timeSpan.Ticks.ToString(CultureInfo.InvariantCulture) + "L)";
            return true;
        }

        if (XamlDateTimeLiteralSemantics.TryParseRoundtrip(rawValue, out var dateTime))
        {
            expression = "global::System.DateTime.FromBinary(" +
                         dateTime.ToBinary().ToString(CultureInfo.InvariantCulture) +
                         "L)";
            return true;
        }

        return false;
    }

    private static string FormatSingleLiteral(float value)
    {
        if (float.IsNaN(value))
        {
            return "global::System.Single.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "global::System.Single.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "global::System.Single.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    private static string FormatDoubleLiteral(double value)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }
}
