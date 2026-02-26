using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;

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
        out bool isExpressionMarkup,
        out string expressionBindingValueExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out string diagnosticId,
        out string diagnosticMessage)
    {
        isExpressionMarkup = false;
        expressionBindingValueExpression = string.Empty;
        accessorExpression = string.Empty;
        normalizedExpression = string.Empty;
        diagnosticId = string.Empty;
        diagnosticMessage = string.Empty;

        if (!ExpressionClassificationService.TryParseCSharpExpressionMarkup(
                value,
                compilation,
                document,
                options.CSharpExpressionsEnabled,
                options.ImplicitCSharpExpressionsEnabled,
                out var csharpExpressionCode,
                out _))
        {
            return false;
        }

        isExpressionMarkup = true;
        if (sourceType is null)
        {
            diagnosticId = "AXSG0110";
            diagnosticMessage = "Expression binding requires x:DataType in scope.";
            return false;
        }

        if (!TryBuildCompiledExpressionAccessorExpression(
                compilation,
                document,
                sourceType,
                csharpExpressionCode,
                out accessorExpression,
                out normalizedExpression,
                out var expressionDependencyNames,
                out var errorMessage))
        {
            diagnosticId = "AXSG0111";
            diagnosticMessage = errorMessage;
            return false;
        }

        if (!TryBuildExpressionBindingRuntimeExpression(
                sourceType,
                accessorExpression,
                expressionDependencyNames,
                out expressionBindingValueExpression))
        {
            diagnosticId = "AXSG0111";
            diagnosticMessage = "expression could not be materialized.";
            return false;
        }

        return true;
    }

    private static bool TryBuildCompiledExpressionAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out ImmutableArray<string> dependencyNames,
        out string errorMessage)
    {
        _ = document;
        accessorExpression = ExpressionSourceParameterName;
        normalizedExpression = rawExpression.Trim();
        dependencyNames = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (!CSharpSourceContextExpressionBuilder.TryBuildAccessorExpression(
                compilation,
                sourceType,
                rawExpression,
                ExpressionSourceParameterName,
                out var result,
                out errorMessage))
        {
            return false;
        }

        accessorExpression = result.AccessorExpression;
        normalizedExpression = result.AccessorExpression;
        dependencyNames = result.DependencyNames;
        return true;
    }

    private static bool TryBuildExpressionBindingRuntimeExpression(
        INamedTypeSymbol sourceType,
        string accessorExpression,
        ImmutableArray<string> dependencyNames,
        out string expression)
    {
        expression = string.Empty;
        if (string.IsNullOrWhiteSpace(accessorExpression))
        {
            return false;
        }

        var dependencyArrayExpression = BuildStringArrayLiteral(dependencyNames);
        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            ">(static " +
            ExpressionSourceParameterName +
            " => (object?)(" +
            accessorExpression +
            "), " +
            dependencyArrayExpression +
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
        return true;
    }

    private static string BuildStringArrayLiteral(ImmutableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<string>()";
        }

        return "new string[] { " +
               string.Join(", ", values.Select(static value => "\"" + Escape(value) + "\"")) +
               " }";
    }
}
