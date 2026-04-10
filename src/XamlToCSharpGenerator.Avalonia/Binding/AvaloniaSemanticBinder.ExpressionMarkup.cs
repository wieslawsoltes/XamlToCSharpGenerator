using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool RequiresStaticResourceResolver(
        ResolvedObjectNode root,
        ImmutableArray<ResolvedStyleDefinition> styles,
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes)
    {
        if (HasStaticResourceResolverRequirement(root))
        {
            return true;
        }

        foreach (var style in styles)
        {
            if (style.Setters.Any(static setter => setter.RequiresStaticResourceResolver))
            {
                return true;
            }
        }

        foreach (var controlTheme in controlThemes)
        {
            if (controlTheme.Setters.Any(static setter => setter.RequiresStaticResourceResolver))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStaticResourceResolverRequirement(ResolvedObjectNode node)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (assignment.RequiresStaticResourceResolver)
            {
                return true;
            }
        }

        foreach (var propertyElement in node.PropertyElementAssignments)
        {
            foreach (var value in propertyElement.ObjectValues)
            {
                if (HasStaticResourceResolverRequirement(value))
                {
                    return true;
                }
            }
        }

        foreach (var child in node.Children)
        {
            if (HasStaticResourceResolverRequirement(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertCSharpExpressionMarkupToBindingExpression(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        string? accessorPlaceholderToken,
        out bool isExpressionMarkup,
        out string expressionBindingValueExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out string? resultTypeName,
        out string diagnosticId,
        out string diagnosticMessage)
    {
        return CSharpExpressionBindingService.TryConvertExpressionMarkupToBindingExpression(
            value,
            compilation,
            document,
            options,
            sourceType,
            accessorPlaceholderToken,
            out isExpressionMarkup,
            out expressionBindingValueExpression,
            out accessorExpression,
            out normalizedExpression,
            out resultTypeName,
            out diagnosticId,
            out diagnosticMessage);
    }

    private static bool TryResolveImplicitCSharpShorthandExpression(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? targetType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out bool isShorthandExpression,
        out CSharpShorthandResolutionResult result)
    {
        return CSharpExpressionBindingService.TryResolveImplicitShorthandExpression(
            value,
            compilation,
            document,
            options,
            sourceType,
            rootTypeSymbol,
            targetType,
            unsafeAccessors,
            out isShorthandExpression,
            out result);
    }

    private static bool TryBuildCompiledBindingRuntimeExpression(
        INamedTypeSymbol sourceType,
        CompiledBindingAccessorResolution resolution,
        string? accessorPlaceholderToken,
        out string expression)
    {
        var sharedResolution = new CompiledBindingAccessorResolutionResult(
            resolution.AccessorExpression,
            resolution.NormalizedPath,
            resolution.ResultTypeName,
            resolution.ResultTypeSymbol,
            resolution.DependencyNames);
        return CSharpExpressionBindingService.TryBuildCompiledBindingRuntimeExpression(
            sourceType,
            sharedResolution,
            accessorPlaceholderToken,
            out expression);
    }

    private static bool TryParseInlineCSharpMarkupExtensionCode(
        string value,
        out string code)
    {
        code = string.Empty;
        if (!TryParseMarkupExtension(value, out var markup) ||
            XamlMarkupExtensionNameSemantics.Classify(markup.Name) != XamlMarkupExtensionKind.CSharp)
        {
            return false;
        }

        var rawCode = TryGetNamedMarkupArgument(markup, "Code") ??
                      (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        code = Unquote(rawCode!);
        return code.Trim().Length > 0;
    }

    private static bool TryBuildInlineCodeBindingExpression(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawCode,
        out string bindingExpression,
        out string normalizedExpression,
        out string? resultTypeName,
        out string errorMessage)
    {
        return CSharpExpressionBindingService.TryBuildInlineCodeBindingExpression(
            compilation,
            sourceType,
            rootType,
            targetType,
            rawCode,
            out bindingExpression,
            out normalizedExpression,
            out resultTypeName,
            out errorMessage);
    }

    private static string GetTypeNameOrObject(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        return (typeSymbol ?? compilation.ObjectType).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
