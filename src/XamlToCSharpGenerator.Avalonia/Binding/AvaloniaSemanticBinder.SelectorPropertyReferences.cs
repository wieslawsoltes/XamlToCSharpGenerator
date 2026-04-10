using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static INamedTypeSymbol? ResolveSelectorTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string typeToken)
    {
        var normalizedTypeToken = typeToken.IndexOf('|') >= 0 && typeToken.IndexOf(':') < 0
            ? typeToken.Replace('|', ':')
            : typeToken;
        return ResolveTypeToken(compilation, document, normalizedTypeToken, document.ClassNamespace);
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
        return FrameworkPropertyReferenceResolutionService.TryResolveReferenceExpression(
            rawValue,
            compilation,
            document,
            defaultOwnerType,
            out expression,
            out propertyValueType);
    }
}
