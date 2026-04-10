using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XamlLiteralConversionPrimitivesService
{
    public delegate bool TryConvertValueExpressionDelegate(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);

    public delegate bool IsTypeAssignableToDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);
    public delegate void TryGetCollectionSplitConfigurationDelegate(
        INamedTypeSymbol collectionType,
        ref string[] separators,
        ref StringSplitOptions splitOptions,
        bool trimEntries);

    private readonly TryConvertValueExpressionDelegate _tryConvertValueExpression;
    private readonly IsTypeAssignableToDelegate _isTypeAssignableTo;
    private readonly TryGetCollectionSplitConfigurationDelegate _tryGetCollectionSplitConfiguration;

    public XamlLiteralConversionPrimitivesService(
        TryConvertValueExpressionDelegate tryConvertValueExpression,
        IsTypeAssignableToDelegate isTypeAssignableTo,
        TryGetCollectionSplitConfigurationDelegate tryGetCollectionSplitConfiguration)
    {
        _tryConvertValueExpression = tryConvertValueExpression ?? throw new ArgumentNullException(nameof(tryConvertValueExpression));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _tryGetCollectionSplitConfiguration = tryGetCollectionSplitConfiguration ?? throw new ArgumentNullException(nameof(tryGetCollectionSplitConfiguration));
    }

    public bool TryConvertEnumValueExpression(
        INamedTypeSymbol enumType,
        string value,
        out string expression)
    {
        expression = string.Empty;
        var tokens = SplitAndTrim(
            value,
            new[] { ",", "|" },
            StringSplitOptions.RemoveEmptyEntries,
            trimEntries: true);
        if (tokens.Length == 0)
        {
            return false;
        }

        var members = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var member = enumType.GetMembers(token).OfType<IFieldSymbol>().FirstOrDefault(static field => field.HasConstantValue);
            if (member is null)
            {
                return false;
            }

            members.Add(enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + member.Name);
        }

        expression = members.Count == 1 ? members[0] : string.Join(" | ", members);
        return true;
    }

    public bool TryConvertStaticPropertyValueExpression(
        ITypeSymbol type,
        string value,
        out string expression)
    {
        expression = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var normalized = trimmed.StartsWith("global::", StringComparison.Ordinal)
            ? trimmed.Substring("global::".Length)
            : trimmed;
        if (normalized.IndexOfAny([' ', '\t', '\r', '\n', ',', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\'']) >= 0)
        {
            return false;
        }

        if (normalized.IndexOf('.') >= 0)
        {
            if (!trimmed.StartsWith("global::", StringComparison.Ordinal))
            {
                return false;
            }

            var segments = normalized.Split('.');
            if (segments.Length < 2)
            {
                return false;
            }

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) ||
                    !SyntaxFacts.IsValidIdentifier(segment))
                {
                    return false;
                }
            }

            expression = trimmed;
            return true;
        }

        if (!SyntaxFacts.IsValidIdentifier(normalized) ||
            type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        for (INamedTypeSymbol? current = namedType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(normalized))
            {
                if (member is IPropertySymbol property &&
                    property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public &&
                    property.GetMethod is not null &&
                    IsAssignableToTarget(property.Type, type))
                {
                    expression = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + property.Name;
                    return true;
                }

                if (member is IFieldSymbol field &&
                    field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    IsAssignableToTarget(field.Type, type))
                {
                    expression = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + field.Name;
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryConvertTimeSpanLiteralExpression(string value, out string expression)
    {
        expression = string.Empty;
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        expression = "global::System.TimeSpan.Parse(\"" +
                     parsed.ToString("c", CultureInfo.InvariantCulture) +
                     "\", global::System.Globalization.CultureInfo.InvariantCulture)";
        return true;
    }

    public bool TryConvertCollectionLiteralExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryGetCollectionElementType(targetType, out var elementType, out var isArrayTarget, out var collectionTypeForSplitConfig))
        {
            return false;
        }

        var separators = new[] { "," };
        var splitOptions = StringSplitOptions.RemoveEmptyEntries;
        if (collectionTypeForSplitConfig is not null)
        {
            _tryGetCollectionSplitConfiguration(
                collectionTypeForSplitConfig,
                ref separators,
                ref splitOptions,
                trimEntries: true);
        }

        var tokens = SplitAndTrim(
            value,
            separators,
            splitOptions,
            trimEntries: true);
        if (tokens.Length == 0)
        {
            return false;
        }

        var itemExpressions = ImmutableArray.CreateBuilder<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!_tryConvertValueExpression(
                    token,
                    elementType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var itemExpression))
            {
                return false;
            }

            itemExpressions.Add(itemExpression);
        }

        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (isArrayTarget)
        {
            expression = "new " + elementTypeName + "[] { " + string.Join(", ", itemExpressions) + " }";
            return true;
        }

        if (targetType is INamedTypeSymbol namedTargetType &&
            CanInstantiateConcreteCollectionType(namedTargetType))
        {
            expression =
                "new " +
                namedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                " { " +
                string.Join(", ", itemExpressions) +
                " }";
            return true;
        }

        expression =
            "new global::System.Collections.Generic.List<" +
            elementTypeName +
            "> { " +
            string.Join(", ", itemExpressions) +
            " }";
        return true;
    }

    public bool TryGetCollectionElementType(
        ITypeSymbol targetType,
        out ITypeSymbol elementType,
        out bool isArrayTarget,
        out INamedTypeSymbol? collectionTypeForSplitConfig)
    {
        collectionTypeForSplitConfig = targetType as INamedTypeSymbol;
        if (targetType is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            isArrayTarget = true;
            return true;
        }

        if (targetType is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.TypeArguments.Length == 1 &&
            (namedType.Name is "IEnumerable" or "IList" or "ICollection" or "List"))
        {
            elementType = namedType.TypeArguments[0];
            isArrayTarget = false;
            return true;
        }

        foreach (var implementedInterface in (targetType as INamedTypeSymbol)?.AllInterfaces ?? ImmutableArray<INamedTypeSymbol>.Empty)
        {
            if (!implementedInterface.IsGenericType || implementedInterface.TypeArguments.Length != 1)
            {
                continue;
            }

            if (implementedInterface.Name is not ("IEnumerable" or "IList" or "ICollection"))
            {
                continue;
            }

            elementType = implementedInterface.TypeArguments[0];
            isArrayTarget = false;
            collectionTypeForSplitConfig = targetType as INamedTypeSymbol;
            return true;
        }

        elementType = null!;
        isArrayTarget = false;
        return false;
    }

    private static string[] SplitAndTrim(
        string value,
        string[] separators,
        StringSplitOptions splitOptions,
        bool trimEntries)
    {
        return XamlDelimitedValueSemantics.SplitCollectionItems(
            value,
            separators,
            trimEntries
                ? splitOptions | (StringSplitOptions)2
                : splitOptions).ToArray();
    }

    private bool IsAssignableToTarget(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        return SymbolEqualityComparer.Default.Equals(sourceType, targetType) ||
               SymbolEqualityComparer.Default.Equals(
                   sourceType.WithNullableAnnotation(NullableAnnotation.None),
                   targetType.WithNullableAnnotation(NullableAnnotation.None)) ||
               _isTypeAssignableTo(sourceType, targetType);
    }

    private static bool CanInstantiateConcreteCollectionType(INamedTypeSymbol targetType)
    {
        if (targetType.TypeKind is TypeKind.Interface or TypeKind.TypeParameter ||
            targetType.IsAbstract)
        {
            return false;
        }

        if (targetType.TypeKind == TypeKind.Struct)
        {
            return true;
        }

        foreach (var constructor in targetType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }
}
