using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static INamedTypeSymbol? ResolveSelectorTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string typeToken)
    {
        return ResolveTypeToken(compilation, document, typeToken, document.ClassNamespace);
    }

    private static INamedTypeSymbol? ResolveWildcardSelectorType(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("Avalonia.StyledElement");
    }

    private static bool TryResolvePropertyReference(
        string propertyToken,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out AvaloniaSelectorPropertyReferenceResolution resolution)
    {
        resolution = default;
        if (!TryResolveAvaloniaPropertyReferenceExpression(
                propertyToken,
                compilation,
                document,
                defaultOwnerType,
                out var propertyExpression,
                out var propertyValueType))
        {
            return false;
        }

        resolution = new AvaloniaSelectorPropertyReferenceResolution(propertyExpression, propertyValueType);
        return true;
    }

    private static bool TryConvertSelectorTypedValue(
        string selectorValue,
        ITypeSymbol propertyValueType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string convertedExpression)
    {
        if (TryConvertValueConversion(
                selectorValue,
                propertyValueType,
                compilation,
                document,
                defaultOwnerType,
                BindingPriorityScope.Style,
                out var converted,
                preferTypedStaticResourceCoercion: false,
                allowObjectStringLiteralFallback: propertyValueType.SpecialType == SpecialType.System_Object,
                allowStaticParseMethodFallback: false))
        {
            convertedExpression = converted.Expression;
            return true;
        }

        convertedExpression = string.Empty;
        return false;
    }

    private static bool TryResolveAvaloniaPropertyReferenceExpression(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string expression)
    {
        return TryResolveAvaloniaPropertyReferenceExpression(
            rawValue,
            compilation,
            document,
            defaultOwnerType,
            out expression,
            out _);
    }

    private static bool TryResolveAvaloniaPropertyReferenceExpression(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string expression,
        out ITypeSymbol? propertyValueType)
    {
        expression = string.Empty;
        propertyValueType = null;
        var token = rawValue.Trim();
        if (token.Length > 2 &&
            token[0] == '(' &&
            token[^1] == ')')
        {
            token = token.Substring(1, token.Length - 2).Trim();
        }

        if (token.Length == 0)
        {
            return false;
        }

        var ownerType = defaultOwnerType;
        var propertyName = token;

        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                token,
                out var ownerToken,
                out var normalizedPropertyName))
        {
            ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace) ?? ownerType;
            propertyName = normalizedPropertyName;
        }

        if (ownerType is null ||
            !TryFindAvaloniaPropertyField(ownerType, propertyName, out var resolvedOwnerType, out var propertyField))
        {
            return false;
        }

        expression = resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + propertyField.Name;
        propertyValueType = TryGetAvaloniaPropertyValueType(propertyField.Type);
        return true;
    }
}
