using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static INamedTypeSymbol? ResolveNodeDataType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        INamedTypeSymbol? nodeType,
        INamedTypeSymbol? inheritedDataType,
        GeneratorOptions options,
        BindingScopeContext scopeContext,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        return BindingScopeDataTypeInferenceService.ResolveNodeDataType(
            compilation,
            document,
            node,
            nodeType,
            inheritedDataType,
            options,
            scopeContext,
            unsafeAccessors);
    }

    private static string ResolveXBindDefaultMode(XamlObjectNode node, string inheritedMode)
    {
        return BindingScopeDataTypeInferenceService.ResolveXBindDefaultMode(node, inheritedMode);
    }

    private static bool IsXBindDefaultModeDirective(XamlPropertyAssignment assignment)
    {
        return BindingScopeDataTypeInferenceService.IsXBindDefaultModeDirective(assignment);
    }

    private static bool IsSupportedXBindMode(string? value)
    {
        return BindingScopeDataTypeInferenceService.IsSupportedXBindMode(value);
    }

    private static string NormalizeXBindMode(string? value)
    {
        return BindingScopeDataTypeInferenceService.NormalizeXBindMode(value);
    }

    private static INamedTypeSymbol? ResolveAssignmentBindingDataType(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol? ownerType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? nodeDataType,
        GeneratorOptions options,
        BindingScopeContext scopeContext,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        return BindingScopeDataTypeInferenceService.ResolveAssignmentBindingDataType(
            assignment,
            ownerType,
            compilation,
            document,
            inheritedDataType,
            nodeDataType,
            options,
            scopeContext,
            unsafeAccessors);
    }

    private static bool TryResolveBindingResultTypeForScopeInference(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? nodeType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        out ITypeSymbol? resultType)
    {
        resultType = null;

        if (!TryResolveBindingSourceTypeForScopeInference(
                compilation,
                document,
                bindingMarkup,
                inheritedDataType,
                nodeType,
                out var sourceType,
                out _) ||
            sourceType is null ||
            !TryBuildCompiledBindingAccessorExpression(
                compilation,
                document,
                sourceType,
                bindingMarkup.Path,
                targetPropertyType: null,
                unsafeAccessors,
                out var resolution,
                out _))
        {
            return false;
        }

        resultType = resolution.ResultTypeSymbol;
        return resultType is not null;
    }

    private static bool TryResolveImplicitCSharpShorthandResultType(
        Compilation compilation,
        XamlDocumentModel document,
        string value,
        GeneratorOptions options,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? ownerType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        out ITypeSymbol? resultType)
    {
        resultType = null;

        if (!TryResolveImplicitCSharpShorthandExpression(
                value,
                compilation,
                document,
                options,
                nodeDataType,
                rootTypeSymbol,
                ownerType,
                unsafeAccessors,
                out var isShorthandExpression,
                out var shorthandResolution) ||
            !isShorthandExpression ||
            shorthandResolution.Path is null)
        {
            return false;
        }

        INamedTypeSymbol? shorthandSourceType = shorthandResolution.Kind switch
        {
            CSharpShorthandResolutionKind.BindingPath => nodeDataType,
            CSharpShorthandResolutionKind.RootExpression => rootTypeSymbol,
            _ => null
        };

        if (shorthandSourceType is null ||
            !TryBuildCompiledBindingAccessorExpression(
                compilation,
                document,
                shorthandSourceType,
                shorthandResolution.Path,
                targetPropertyType: null,
                unsafeAccessors,
                out var resolution,
                out _))
        {
            return false;
        }

        resultType = resolution.ResultTypeSymbol;
        return resultType is not null;
    }

    private static string ResolveAliasedPropertyName(INamedTypeSymbol ownerType, string propertyName)
    {
        return ResolvePropertyAlias(ownerType, propertyName).ResolvedPropertyName;
    }

    private static bool TryGetCollectionElementTypeForInference(
        ITypeSymbol collectionType,
        out ITypeSymbol? elementType)
    {
        elementType = null;
        if (!TryGetCollectionElementType(collectionType, out var resolvedElementType, out _, out _))
        {
            return false;
        }

        elementType = resolvedElementType;
        return elementType is not null;
    }

    private static bool TryParseBindingMarkupFromObjectNode(
        XamlObjectNode node,
        out BindingMarkup bindingMarkup)
    {
        return BindingObjectNodeMarkupParser.TryParseBindingMarkupFromObjectNode(node, out bindingMarkup);
    }

    private static bool TryGetSingleBindingObjectNodeArgumentValue(
        XamlPropertyElement propertyElement,
        bool allowTypeExpressionExtraction,
        out string value)
    {
        return BindingObjectNodeMarkupParser.TryGetSingleBindingObjectNodeArgumentValue(
            propertyElement,
            allowTypeExpressionExtraction,
            out value);
    }

    private static bool TryExtractTypeExpressionFromXamlTypeNode(
        XamlObjectNode node,
        out string typeExpression)
    {
        return BindingObjectNodeMarkupParser.TryExtractTypeExpressionFromXamlTypeNode(node, out typeExpression);
    }

    private static string? GetCanonicalBindingObjectNodeArgumentName(string propertyName)
    {
        return BindingObjectNodeMarkupParser.GetCanonicalBindingObjectNodeArgumentName(propertyName);
    }

    private static bool TryGetSingleMarkupExtensionArgumentValue(XamlObjectNode node, out string value)
    {
        return BindingObjectNodeMarkupParser.TryGetSingleMarkupExtensionArgumentValue(node, out value);
    }

    private static bool IsXamlArrayNode(XamlObjectNode node)
    {
        return BindingObjectNodeMarkupParser.IsXamlArrayNode(node);
    }

    private static bool IsXamlTypeNode(XamlObjectNode node)
    {
        return BindingObjectNodeMarkupParser.IsXamlTypeNode(node);
    }

    private static ResolvedObjectNode BindXamlTypeNode(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options)
    {
        return XamlTypeNodeBindingService.BindXamlTypeNode(
            node,
            compilation,
            document,
            options.StrictMode,
            diagnostics.Add);
    }
}
