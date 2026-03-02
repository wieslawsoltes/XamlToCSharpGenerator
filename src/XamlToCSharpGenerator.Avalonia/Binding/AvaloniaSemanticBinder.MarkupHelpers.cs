using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryConvertXamlPrimitiveMarkupExtension(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression)
    {
        expression = string.Empty;
        var kind = XamlMarkupExtensionNameSemantics.Classify(markup.Name);
        var rawValue = TryGetNamedMarkupArgument(markup, "Value") ??
                       (markup.PositionalArguments.Length > 0 ? XamlQuotedValueSemantics.TrimAndUnquote(markup.PositionalArguments[0]) : null);
        switch (kind)
        {
            case XamlMarkupExtensionKind.True:
                expression = "true";
                return true;
            case XamlMarkupExtensionKind.False:
                expression = "false";
                return true;
            case XamlMarkupExtensionKind.String:
            {
                var value = rawValue ?? string.Empty;
                expression = "\"" + Escape(value ?? string.Empty) + "\"";
                return true;
            }
            case XamlMarkupExtensionKind.Char:
            {
                if (string.IsNullOrEmpty(rawValue))
                {
                    return false;
                }

                var trimmedValue = rawValue!.Trim();
                if (trimmedValue.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) &&
                    trimmedValue.Length == 6 &&
                    int.TryParse(trimmedValue.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicodeCode))
                {
                    expression = "'\\u" + unicodeCode.ToString("x4", CultureInfo.InvariantCulture) + "'";
                    return true;
                }

                if (trimmedValue.Length != 1)
                {
                    return false;
                }

                expression = "'" + EscapeChar(trimmedValue[0]) + "'";
                return true;
            }
            case XamlMarkupExtensionKind.Byte:
            {
                if (!byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((byte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.SByte:
            {
                if (!sbyte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((sbyte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.Int16:
            {
                if (!short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((short)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.UInt16:
            {
                if (!ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((ushort)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.Int32:
            {
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            case XamlMarkupExtensionKind.UInt32:
            {
                if (!uint.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            }
            case XamlMarkupExtensionKind.Int64:
            {
                if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            }
            case XamlMarkupExtensionKind.UInt64:
            {
                if (!ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            }
            case XamlMarkupExtensionKind.Single:
            case XamlMarkupExtensionKind.Double:
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = targetType.SpecialType == SpecialType.System_Single
                    ? parsed.ToString("R", CultureInfo.InvariantCulture) + "f"
                    : parsed.ToString("R", CultureInfo.InvariantCulture) + "d";
                return true;
            }
            case XamlMarkupExtensionKind.Decimal:
            {
                if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "m";
                return true;
            }
            case XamlMarkupExtensionKind.DateTime:
            {
                if (rawValue is null)
                {
                    return false;
                }

                var normalizedRawValue = rawValue.Trim();
                if (normalizedRawValue.Length == 0)
                {
                    return false;
                }

                if (!XamlDateTimeLiteralSemantics.TryParseRoundtrip(normalizedRawValue, out var parsedDateTime))
                {
                    return false;
                }

                expression = "global::System.DateTime.FromBinary(" +
                             parsedDateTime.ToBinary().ToString(CultureInfo.InvariantCulture) +
                             "L)";
                return true;
            }
            case XamlMarkupExtensionKind.TimeSpan:
            {
                if (rawValue is null)
                {
                    return false;
                }

                var normalizedRawValue = rawValue.Trim();
                if (normalizedRawValue.Length == 0)
                {
                    return false;
                }

                if (!XamlTimeSpanLiteralSemantics.TryParse(normalizedRawValue, out var parsedTimeSpan))
                {
                    return false;
                }

                expression = "global::System.TimeSpan.FromTicks(" +
                             parsedTimeSpan.Ticks.ToString(CultureInfo.InvariantCulture) +
                             "L)";
                return true;
            }
            case XamlMarkupExtensionKind.Uri:
            {
                if (rawValue is null)
                {
                    return false;
                }

                var normalizedRawValue = rawValue.Trim();
                if (normalizedRawValue.Length == 0)
                {
                    return false;
                }

                expression = "new global::System.Uri(\"" + Escape(normalizedRawValue) + "\", global::System.UriKind.RelativeOrAbsolute)";
                return true;
            }
            case XamlMarkupExtensionKind.Null:
                expression = "null";
                return true;
            default:
                return false;
        }
    }

    private static bool TryConvertGenericMarkupExtensionExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryResolveMarkupExtensionType(compilation, document, markup.Name, out var extensionType))
        {
            return false;
        }

        if (extensionType is null)
        {
            return false;
        }

        var constructor = extensionType.InstanceConstructors
            .Where(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                !candidate.IsStatic)
            .Where(candidate => candidate.Parameters.Length == markup.PositionalArguments.Length)
            .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();

        if (constructor is null && markup.PositionalArguments.Length > 0)
        {
            return false;
        }

        var positionalExpressions = new List<string>(markup.PositionalArguments.Length);
        for (var index = 0; index < markup.PositionalArguments.Length; index++)
        {
            var positionalArgument = markup.PositionalArguments[index];
            var positionalTargetType = constructor?.Parameters[index].Type;
            if (!TryConvertMarkupArgumentExpression(
                    positionalArgument,
                    positionalTargetType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var positionalExpression))
            {
                return false;
            }

            positionalExpressions.Add(positionalExpression);
        }

        var constructorExpression = "new " + extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "(" +
                                    string.Join(", ", positionalExpressions) +
                                    ")";

        var initializerExpressions = new List<string>();
        foreach (var namedArgument in markup.NamedArguments)
        {
            var property = extensionType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate =>
                    candidate.SetMethod is not null &&
                    candidate.Name.Equals(namedArgument.Key, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                return false;
            }

            if (!TryConvertMarkupArgumentExpression(
                    namedArgument.Value,
                    property.Type,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var propertyExpression))
            {
                return false;
            }

            initializerExpressions.Add(property.Name + " = " + propertyExpression);
        }

        if (initializerExpressions.Count > 0)
        {
            constructorExpression += " { " + string.Join(", ", initializerExpressions) + " }";
        }

        var runtimeProvideValueExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(" +
            constructorExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", " +
            MarkupContextBaseUriToken +
            ", " +
            MarkupContextParentStackToken +
            ")";

        expression = WrapWithTargetTypeCast(targetType, runtimeProvideValueExpression);
        return true;
    }

    private static bool TryConvertMarkupArgumentExpression(
        string rawValue,
        ITypeSymbol? targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            expression = "null";
            return true;
        }

        var conversionTargetType = targetType ??
                                   ResolveContractType(compilation, TypeContractId.SystemObject) ??
                                   compilation.ObjectType;
        if (conversionTargetType is null)
        {
            return false;
        }

        if (TryConvertValueExpression(
                Unquote(value),
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (TryConvertMarkupExtensionExpression(
                value,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            expression = "\"" + Escape(Unquote(value)) + "\"";
            return true;
        }

        return false;
    }

    private static bool TryResolveMarkupExtensionType(
        Compilation compilation,
        XamlDocumentModel document,
        string markupName,
        out INamedTypeSymbol? extensionType)
    {
        extensionType = null;
        if (XamlMarkupExtensionNameSemantics.Classify(markupName) == XamlMarkupExtensionKind.Null)
        {
            return false;
        }

        var token = XamlMarkupExtensionNameSemantics.ToClrExtensionTypeToken(markupName);
        if (token.Length == 0)
        {
            return false;
        }

        extensionType = ResolveTypeToken(compilation, document, token, document.ClassNamespace);
        if (extensionType is null)
        {
            return false;
        }

        var markupExtensionBase = ResolveContractType(compilation, TypeContractId.AvaloniaMarkupExtensionBase);
        if (markupExtensionBase is null)
        {
            return false;
        }

        return IsTypeAssignableTo(extensionType, markupExtensionBase);
    }

    private static string WrapWithTargetTypeCast(ITypeSymbol targetType, string expression)
    {
        if (targetType.SpecialType == SpecialType.System_Object)
        {
            return expression;
        }

        var typedTargetExpression = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<" +
               typedTargetExpression +
               ">(" +
               expression +
               ")";
    }

    private static bool TryConvertByStaticParseMethod(ITypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var parseMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, namedType));
        if (parseMethod is not null)
        {
            expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         ".Parse(\"" + Escape(value) + "\")";
            return true;
        }

        var parseWithCultureMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.Parameters.Length == 2 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsCultureAwareParseParameter(method.Parameters[1].Type) &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, namedType));
        if (parseWithCultureMethod is null)
        {
            return false;
        }

        expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ".Parse(\"" + Escape(value) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
        return true;
    }

    private static bool IsCultureAwareParseParameter(ITypeSymbol type)
    {
        var fullyQualifiedTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullyQualifiedTypeName is
            "global::System.IFormatProvider" or
            "global::System.IFormatProvider?" or
            "global::System.Globalization.CultureInfo" or
            "global::System.Globalization.CultureInfo?";
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveStaticMemberExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string memberToken,
        out string expression)
    {
        expression = string.Empty;
        if (!XamlTokenSplitSemantics.TrySplitAtLastSeparator(
                memberToken,
                '.',
                out var ownerToken,
                out var memberName))
        {
            return false;
        }

        var ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        if (ownerType is null)
        {
            return false;
        }

        var staticField = ownerType.GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.IsStatic);
        if (staticField is not null)
        {
            expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + staticField.Name;
            return true;
        }

        var staticProperty = ownerType.GetMembers(memberName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.IsStatic);
        if (staticProperty is not null)
        {
            expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + staticProperty.Name;
            return true;
        }

        return false;
    }
}
