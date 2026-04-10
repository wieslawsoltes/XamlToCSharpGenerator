using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TypedLiteralValueConversionService
{
    public delegate ITypeSymbol? ResolveTypeFromTypeExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace);

    public delegate bool TryConvertTimeSpanLiteralExpressionDelegate(string value, out string expression);
    public delegate bool TryConvertStaticPropertyValueExpressionDelegate(ITypeSymbol type, string value, out string expression);
    public delegate bool TryConvertCollectionLiteralExpressionDelegate(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);
    public delegate bool TryConvertEnumValueExpressionDelegate(INamedTypeSymbol enumType, string value, out string expression);
    public delegate bool TryConvertFrameworkSpecificLiteralExpressionDelegate(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);
    public delegate bool TryConvertByTypeConverterDelegate(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression,
        out ResolvedValueRequirements requirements,
        ImmutableArray<AttributeData> converterAttributes);
    public delegate bool TryConvertByStaticParseMethodDelegate(ITypeSymbol type, string value, out string expression);

    private readonly Func<string, string> _unescapeEscapedLiteral;
    private readonly Func<string, string> _escape;
    private readonly ResolveTypeFromTypeExpressionDelegate _resolveTypeFromTypeExpression;
    private readonly TryConvertTimeSpanLiteralExpressionDelegate _tryConvertTimeSpanLiteralExpression;
    private readonly TryConvertStaticPropertyValueExpressionDelegate _tryConvertStaticPropertyValueExpression;
    private readonly TryConvertCollectionLiteralExpressionDelegate _tryConvertCollectionLiteralExpression;
    private readonly TryConvertEnumValueExpressionDelegate _tryConvertEnumValueExpression;
    private readonly TryConvertFrameworkSpecificLiteralExpressionDelegate _tryConvertFrameworkSpecificLiteralExpression;
    private readonly TryConvertByTypeConverterDelegate _tryConvertByTypeConverter;
    private readonly TryConvertByStaticParseMethodDelegate _tryConvertByStaticParseMethod;

    public TypedLiteralValueConversionService(
        Func<string, string> unescapeEscapedLiteral,
        Func<string, string> escape,
        ResolveTypeFromTypeExpressionDelegate resolveTypeFromTypeExpression,
        TryConvertTimeSpanLiteralExpressionDelegate tryConvertTimeSpanLiteralExpression,
        TryConvertStaticPropertyValueExpressionDelegate tryConvertStaticPropertyValueExpression,
        TryConvertCollectionLiteralExpressionDelegate tryConvertCollectionLiteralExpression,
        TryConvertEnumValueExpressionDelegate tryConvertEnumValueExpression,
        TryConvertFrameworkSpecificLiteralExpressionDelegate tryConvertFrameworkSpecificLiteralExpression,
        TryConvertByTypeConverterDelegate tryConvertByTypeConverter,
        TryConvertByStaticParseMethodDelegate tryConvertByStaticParseMethod)
    {
        _unescapeEscapedLiteral = unescapeEscapedLiteral ?? throw new ArgumentNullException(nameof(unescapeEscapedLiteral));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
        _resolveTypeFromTypeExpression = resolveTypeFromTypeExpression ?? throw new ArgumentNullException(nameof(resolveTypeFromTypeExpression));
        _tryConvertTimeSpanLiteralExpression = tryConvertTimeSpanLiteralExpression ?? throw new ArgumentNullException(nameof(tryConvertTimeSpanLiteralExpression));
        _tryConvertStaticPropertyValueExpression = tryConvertStaticPropertyValueExpression ?? throw new ArgumentNullException(nameof(tryConvertStaticPropertyValueExpression));
        _tryConvertCollectionLiteralExpression = tryConvertCollectionLiteralExpression ?? throw new ArgumentNullException(nameof(tryConvertCollectionLiteralExpression));
        _tryConvertEnumValueExpression = tryConvertEnumValueExpression ?? throw new ArgumentNullException(nameof(tryConvertEnumValueExpression));
        _tryConvertFrameworkSpecificLiteralExpression = tryConvertFrameworkSpecificLiteralExpression ?? throw new ArgumentNullException(nameof(tryConvertFrameworkSpecificLiteralExpression));
        _tryConvertByTypeConverter = tryConvertByTypeConverter ?? throw new ArgumentNullException(nameof(tryConvertByTypeConverter));
        _tryConvertByStaticParseMethod = tryConvertByStaticParseMethod ?? throw new ArgumentNullException(nameof(tryConvertByStaticParseMethod));
    }

    public bool TryConvert(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback,
        bool allowStaticParseMethodFallback,
        ImmutableArray<AttributeData> converterAttributes)
    {
        conversion = default;
        var rawValue = _unescapeEscapedLiteral(value?.Trim() ?? string.Empty);

        if (type.SpecialType == SpecialType.System_String)
        {
            conversion = new ResolvedValueConversionResult(
                Expression: "\"" + _escape(rawValue) + "\"",
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (TryConvertPrimitive(type, rawValue, out var primitiveExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: primitiveExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (type is INamedTypeSymbol namedEnum && namedEnum.TypeKind == TypeKind.Enum &&
            _tryConvertEnumValueExpression(namedEnum, rawValue, out var enumExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: enumExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.TimeSpan" &&
            _tryConvertTimeSpanLiteralExpression(rawValue, out var timeSpanExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: timeSpanExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Type")
        {
            var resolvedType = _resolveTypeFromTypeExpression(
                compilation,
                document,
                rawValue,
                document.ClassNamespace);
            if (resolvedType is not null)
            {
                conversion = new ResolvedValueConversionResult(
                    Expression: "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
                    ValueKind: ResolvedValueKind.Literal);
                return true;
            }
        }

        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Globalization.CultureInfo")
        {
            conversion = new ResolvedValueConversionResult(
                Expression: "global::System.Globalization.CultureInfo.GetCultureInfo(\"" + _escape(rawValue) + "\")",
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (_tryConvertStaticPropertyValueExpression(type, rawValue, out var staticPropertyExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: staticPropertyExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (_tryConvertCollectionLiteralExpression(
                type,
                rawValue,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var collectionExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: collectionExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (_tryConvertFrameworkSpecificLiteralExpression(
                type,
                rawValue,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var frameworkSpecificExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: frameworkSpecificExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (_tryConvertByTypeConverter(
                type,
                rawValue,
                compilation,
                out var typeConverterExpression,
                out var requirements,
                converterAttributes))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: typeConverterExpression,
                ValueKind: ResolvedValueKind.Literal,
                ValueRequirements: requirements);
            return true;
        }

        if (allowStaticParseMethodFallback &&
            _tryConvertByStaticParseMethod(type, rawValue, out var parseExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: parseExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        if (allowObjectStringLiteralFallback &&
            type.SpecialType == SpecialType.System_Object)
        {
            var objectLiteral = XamlQuotedValueSemantics.TrimAndUnquote(rawValue);
            conversion = new ResolvedValueConversionResult(
                Expression: "\"" + _escape(objectLiteral) + "\"",
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        return false;
    }

    private static bool TryConvertPrimitive(
        ITypeSymbol type,
        string value,
        out string expression)
    {
        expression = string.Empty;
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean when bool.TryParse(value, out var boolValue):
                expression = boolValue ? "true" : "false";
                return true;

            case SpecialType.System_Int32 when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue):
                expression = intValue.ToString(CultureInfo.InvariantCulture);
                return true;

            case SpecialType.System_Int64 when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue):
                expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
                return true;

            case SpecialType.System_Single when float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue):
                expression = FormatSingleLiteral(floatValue);
                return true;

            case SpecialType.System_Double when double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue):
                expression = FormatDoubleLiteral(doubleValue);
                return true;

            case SpecialType.System_Decimal when decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var decimalValue):
                expression = decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
                return true;

            default:
                return false;
        }
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
