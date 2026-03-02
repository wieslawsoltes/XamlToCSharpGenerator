using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal static class AvaloniaSelectorSemanticAdapter
{
    private static readonly ISelectorExpressionEmitter SelectorExpressionEmitter = new AvaloniaSelectorExpressionEmitter();

    public static bool IsSelectorType(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display == "global::Avalonia.Styling.Selector" ||
               display == "global::Avalonia.Styling.Selector?";
    }

    public static bool TryBuildSelectorExpression(
        string selector,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> resolveTypeToken,
        TryResolveSelectorPropertyReference tryResolvePropertyReference,
        TryConvertSelectorUntypedValue tryConvertUntypedValue,
        TryConvertSelectorTypedValue tryConvertTypedValue,
        out string expression)
    {
        return SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            selector,
            selectorTypeFallback,
            selectorNestingTypeHint,
            typeToken => resolveTypeToken(compilation, document, typeToken),
            SelectorExpressionEmitter,
            TryResolvePropertyPredicate,
            out expression);

        bool TryResolvePropertyPredicate(
            string predicateText,
            INamedTypeSymbol? defaultOwnerType,
            out string propertyExpression,
            out string valueExpression)
        {
            return AvaloniaSelectorPropertyPredicateResolver.TryResolve(
                predicateText,
                compilation,
                document,
                defaultOwnerType,
                tryResolvePropertyReference,
                tryConvertUntypedValue,
                tryConvertTypedValue,
                out propertyExpression,
                out valueExpression);
        }
    }

    public static string? TryExtractSelectorTypeToken(string selector)
    {
        var validation = SelectorSyntaxValidator.Validate(selector);
        if (!validation.IsValid || validation.Branches.Length != 1)
        {
            return null;
        }

        return validation.Branches[0].LastTypeToken;
    }

    public static INamedTypeSymbol? TryResolveSelectorTargetType(
        ImmutableArray<SelectorBranchInfo> branches,
        Func<string, INamedTypeSymbol?> resolveTypeToken,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        out string? unresolvedTypeToken,
        out int unresolvedTypeOffset)
    {
        var resolution = SelectorTargetTypeResolutionSemantics.ResolveTargetType(
            branches,
            resolveTypeToken,
            isTypeAssignableTo);
        unresolvedTypeToken = resolution.UnresolvedTypeToken;
        unresolvedTypeOffset = resolution.UnresolvedTypeOffset;
        return resolution.TargetType;
    }
}
