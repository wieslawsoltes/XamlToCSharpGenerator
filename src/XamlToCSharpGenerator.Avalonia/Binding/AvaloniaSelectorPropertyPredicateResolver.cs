using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal readonly struct AvaloniaSelectorPropertyReferenceResolution
{
    public AvaloniaSelectorPropertyReferenceResolution(string propertyExpression, ITypeSymbol? propertyValueType)
    {
        PropertyExpression = propertyExpression;
        PropertyValueType = propertyValueType;
    }

    public string PropertyExpression { get; }

    public ITypeSymbol? PropertyValueType { get; }
}

internal delegate bool TryResolveSelectorPropertyReference(
    string propertyToken,
    Compilation compilation,
    XamlDocumentModel document,
    INamedTypeSymbol? defaultOwnerType,
    out AvaloniaSelectorPropertyReferenceResolution resolution);

internal delegate bool TryConvertSelectorUntypedValue(string selectorValue, out string valueExpression);

internal delegate bool TryConvertSelectorTypedValue(
    string selectorValue,
    ITypeSymbol propertyValueType,
    Compilation compilation,
    XamlDocumentModel document,
    INamedTypeSymbol? defaultOwnerType,
    out string valueExpression);

internal static class AvaloniaSelectorPropertyPredicateResolver
{
    public static bool TryResolve(
        string predicateText,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        TryResolveSelectorPropertyReference tryResolvePropertyReference,
        TryConvertSelectorUntypedValue tryConvertUntypedValue,
        TryConvertSelectorTypedValue tryConvertTypedValue,
        out string propertyExpression,
        out string valueExpression)
    {
        propertyExpression = string.Empty;
        valueExpression = string.Empty;

        if (!SelectorPropertyPredicateSyntax.TryParse(predicateText, out var predicate))
        {
            return false;
        }

        if (!tryResolvePropertyReference(
                predicate.PropertyToken,
                compilation,
                document,
                defaultOwnerType,
                out var propertyResolution))
        {
            return false;
        }

        propertyExpression = propertyResolution.PropertyExpression;
        var selectorValue = XamlQuotedValueSemantics.UnquoteWrapped(predicate.RawValue);
        if (propertyResolution.PropertyValueType is null)
        {
            return tryConvertUntypedValue(selectorValue, out valueExpression);
        }

        return tryConvertTypedValue(
            selectorValue,
            propertyResolution.PropertyValueType,
            compilation,
            document,
            defaultOwnerType,
            out valueExpression);
    }

}
