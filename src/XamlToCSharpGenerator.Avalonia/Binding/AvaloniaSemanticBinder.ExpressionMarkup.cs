using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private enum CSharpShorthandResolutionKind
    {
        None = 0,
        BindingPath = 1,
        RootExpression = 2,
        Conflict = 3
    }

    private readonly record struct CSharpShorthandResolutionResult(
        CSharpShorthandResolutionKind Kind,
        string? Path,
        string? ValueExpression,
        string? AccessorExpression,
        string? SourceTypeName,
        string? ResultTypeName,
        string? DiagnosticId,
        string? DiagnosticMessage);

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
        out string? resultTypeName,
        out string diagnosticId,
        out string diagnosticMessage)
    {
        isExpressionMarkup = false;
        expressionBindingValueExpression = string.Empty;
        accessorExpression = string.Empty;
        normalizedExpression = string.Empty;
        resultTypeName = null;
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
                out resultTypeName,
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

    private static bool TryResolveImplicitCSharpShorthandExpression(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? targetType,
        out bool isShorthandExpression,
        out CSharpShorthandResolutionResult result)
    {
        isShorthandExpression = false;
        result = default;

        if (!ExpressionClassificationService.TryParseCSharpExpressionMarkup(
                value,
                compilation,
                document,
                options.CSharpExpressionsEnabled,
                options.ImplicitCSharpExpressionsEnabled,
                out var csharpExpressionCode,
                out var isExplicitExpression) ||
            isExplicitExpression ||
            !CSharpMarkupExpressionSemantics.TryParseSimpleShorthandPath(csharpExpressionCode, out var shorthand))
        {
            return false;
        }

        isShorthandExpression = true;

        switch (shorthand.Scope)
        {
            case CSharpShorthandExpressionScope.BindingContext:
                if (sourceType is null)
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.None,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "AXSG0110",
                        "Shorthand binding requires x:DataType in scope.");
                    return true;
                }

                if (TryBuildCompiledBindingAccessorExpression(
                        compilation,
                        document,
                        sourceType,
                        shorthand.Path,
                        out var forcedBindingAccessor,
                        out var forcedBindingPath,
                        out var forcedBindingResultTypeName,
                        out _))
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.BindingPath,
                        forcedBindingPath,
                        null,
                        forcedBindingAccessor,
                        sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        forcedBindingResultTypeName,
                        null,
                        null);
                    return true;
                }

                result = new CSharpShorthandResolutionResult(
                    CSharpShorthandResolutionKind.None,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "AXSG0111",
                    $"Shorthand binding path '{shorthand.RawExpression}' could not be resolved against x:DataType '{sourceType.ToDisplayString()}'.");
                return true;

            case CSharpShorthandExpressionScope.Root:
                if (rootTypeSymbol is null)
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.None,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "AXSG0600",
                        "Root-scoped shorthand requires x:Class-backed root type.");
                    return true;
                }

                if (!TryBuildCompiledBindingAccessorExpression(
                        compilation,
                        document,
                        rootTypeSymbol,
                        shorthand.Path,
                        out _,
                        out var normalizedRootPath,
                        out var rootResultTypeName,
                        out _))
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.None,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "AXSG0111",
                        $"Root-scoped shorthand '{shorthand.RawExpression}' could not be resolved against root type '{rootTypeSymbol.ToDisplayString()}'.");
                    return true;
                }

                if (!TryBuildInlineCodeBindingExpression(
                        compilation,
                        sourceType,
                        rootTypeSymbol,
                        targetType,
                        "root." + normalizedRootPath,
                        out var rootValueExpression,
                        out _,
                        out _,
                        out var rootErrorMessage))
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.None,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "AXSG0111",
                        rootErrorMessage);
                    return true;
                }

                result = new CSharpShorthandResolutionResult(
                    CSharpShorthandResolutionKind.RootExpression,
                    normalizedRootPath,
                    rootValueExpression,
                    "root." + normalizedRootPath,
                    rootTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    rootResultTypeName,
                    null,
                    null);
                return true;

            case CSharpShorthandExpressionScope.Auto:
            {
                var sourceAccessor = string.Empty;
                var sourcePath = string.Empty;
                string? sourceResultTypeName = null;
                var sourceResolved = sourceType is not null &&
                                     TryBuildCompiledBindingAccessorExpression(
                                         compilation,
                                         document,
                                         sourceType,
                                         shorthand.Path,
                                         out sourceAccessor,
                                         out sourcePath,
                                         out sourceResultTypeName,
                                         out _);

                var rootPath = string.Empty;
                string? rootResolvedResultTypeName = null;
                var rootResolved = rootTypeSymbol is not null &&
                                   TryBuildCompiledBindingAccessorExpression(
                                       compilation,
                                       document,
                                       rootTypeSymbol,
                                       shorthand.Path,
                                       out _,
                                       out rootPath,
                                       out rootResolvedResultTypeName,
                                       out _);

                if (sourceResolved && rootResolved && sourceType is not null && rootTypeSymbol is not null)
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.Conflict,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "AXSG0113",
                        $"Shorthand expression '{shorthand.RawExpression}' is ambiguous between x:DataType '{sourceType.ToDisplayString()}' and root type '{rootTypeSymbol.ToDisplayString()}'. Use '.{sourcePath}' or 'this.{rootPath}'.");
                    return true;
                }

                if (sourceResolved && sourceType is not null)
                {
                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.BindingPath,
                        sourcePath,
                        null,
                        sourceAccessor,
                        sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        sourceResultTypeName,
                        null,
                        null);
                    return true;
                }

                if (rootResolved && rootTypeSymbol is not null)
                {
                    if (!TryBuildInlineCodeBindingExpression(
                            compilation,
                            sourceType,
                            rootTypeSymbol,
                            targetType,
                            "root." + rootPath,
                            out var autoRootValueExpression,
                            out _,
                            out _,
                            out var autoRootErrorMessage))
                    {
                        result = new CSharpShorthandResolutionResult(
                            CSharpShorthandResolutionKind.None,
                            null,
                            null,
                            null,
                            null,
                            null,
                            "AXSG0111",
                            autoRootErrorMessage);
                        return true;
                    }

                    result = new CSharpShorthandResolutionResult(
                        CSharpShorthandResolutionKind.RootExpression,
                        rootPath,
                        autoRootValueExpression,
                        "root." + rootPath,
                        rootTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        rootResolvedResultTypeName,
                        null,
                        null);
                    return true;
                }

                return false;
            }

            default:
                return false;
        }
    }

    private static bool TryBuildCompiledExpressionAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out string? resultTypeName,
        out ImmutableArray<string> dependencyNames,
        out string errorMessage)
    {
        _ = document;
        accessorExpression = ExpressionSourceParameterName;
        normalizedExpression = rawExpression.Trim();
        resultTypeName = null;
        dependencyNames = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (!CSharpSourceContextExpressionAnalysisService.TryAnalyze(
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
        normalizedExpression = rawExpression.Trim();
        resultTypeName = result.ResultTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
        bindingExpression = string.Empty;
        normalizedExpression = string.Empty;
        resultTypeName = null;
        errorMessage = string.Empty;

        if (!CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
                compilation,
                sourceType,
                rootType,
                targetType,
                rawCode,
                out var inlineAnalysis,
                out errorMessage))
        {
            return false;
        }

        normalizedExpression = inlineAnalysis.NormalizedExpression;
        resultTypeName = inlineAnalysis.ResultTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bindingExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideInlineCodeBinding<" +
            GetTypeNameOrObject(sourceType, compilation) +
            ", " +
            GetTypeNameOrObject(rootType, compilation) +
            ", " +
            GetTypeNameOrObject(targetType, compilation) +
            ">(static (source, root, target) => (object?)(" +
            inlineAnalysis.NormalizedExpression +
            "), " +
            BuildStringArrayLiteral(inlineAnalysis.DependencyNames) +
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

    private static string GetTypeNameOrObject(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        return (typeSymbol ?? compilation.ObjectType).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
