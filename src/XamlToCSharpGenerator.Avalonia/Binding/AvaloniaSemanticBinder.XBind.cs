using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryBuildXBindBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        XBindMarkup xBindMarkup,
        INamedTypeSymbol? ambientDataContextType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        ITypeSymbol? bindingValueType,
        BindingPriorityScope bindingPriorityScope,
        bool isInsideDataTemplate,
        string defaultMode,
        out string bindingExpression,
        out string? resultTypeName,
        out string errorCode,
        out string errorMessage)
    {
        bindingExpression = string.Empty;
        resultTypeName = null;
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (rootType is null)
        {
            errorCode = "AXSG0116";
            errorMessage = "x:Bind requires an x:Class-backed root type.";
            return false;
        }

        if (!TryResolveExplicitXBindSourceType(
                compilation,
                document,
                xBindMarkup.DataType,
                out var explicitSourceType,
                out errorMessage))
        {
            errorCode = "AXSG0110";
            return false;
        }

        var sourceType = explicitSourceType ?? (isInsideDataTemplate ? ambientDataContextType : rootType);
        var ambientDataContextSourceType = explicitSourceType ?? ambientDataContextType;

        if (sourceType is null)
        {
            errorCode = "AXSG0110";
            errorMessage = "x:Bind requires x:DataType in scope when used inside a DataTemplate.";
            return false;
        }

        if (xBindMarkup.HasSourceConflict)
        {
            errorCode = "AXSG0111";
            errorMessage = xBindMarkup.SourceConflictMessage ?? "x:Bind source configuration is invalid.";
            return false;
        }

        var effectiveMode = NormalizeXBindMode(xBindMarkup.Mode ?? defaultMode);
        if (!IsSupportedXBindMode(effectiveMode))
        {
            errorCode = "AXSG0115";
            errorMessage = $"x:Bind mode '{xBindMarkup.Mode ?? defaultMode}' is not supported. Use OneTime, OneWay, or TwoWay.";
            return false;
        }

        if (!TryMapBindingMode(effectiveMode, out var bindingModeExpression))
        {
            errorCode = "AXSG0115";
            errorMessage = $"x:Bind mode '{effectiveMode}' could not be mapped to an Avalonia binding mode.";
            return false;
        }

        var baseSourceReference = isInsideDataTemplate
            ? new XBindPathReference(XBindSourceReferenceKind.DataContext, ".", null, null, null)
            : new XBindPathReference(XBindSourceReferenceKind.Root, ".", null, null, null);
        if (!TryResolveXBindSourceConfiguration(
                compilation,
                document,
                currentNode,
                xBindMarkup,
                sourceType,
                ambientDataContextSourceType,
                rootType,
                targetType,
                baseSourceReference,
                out var sourceConfiguration,
                out errorMessage))
        {
            errorCode = "AXSG0117";
            errorMessage = "x:Bind source configuration is invalid: " + errorMessage;
            return false;
        }

        sourceType = sourceConfiguration.SourceType;
        var loweringContext = new XBindLoweringContext(
            compilation,
            document,
            currentNode,
            sourceType,
            rootType,
            targetType,
            sourceConfiguration.SourceReference);

        XBindExpressionNode? xBindExpression = null;
        XBindLoweredExpression loweredExpression;
        if (string.IsNullOrWhiteSpace(xBindMarkup.Path))
        {
            loweredExpression = new XBindLoweredExpression("source", IsTypeReference: false);
        }
        else
        {
            if (!XBindExpressionParser.TryParse(xBindMarkup.Path, out xBindExpression, out var parseError) ||
                xBindExpression is null)
            {
                errorCode = "AXSG0117";
                errorMessage = $"x:Bind expression '{xBindMarkup.Path}' is invalid: {parseError}";
                return false;
            }

            if (!XBindExpressionSemanticService.TryLowerExpression(
                    xBindExpression,
                    loweringContext,
                    out loweredExpression,
                    out errorMessage))
            {
                errorCode = "AXSG0117";
                return false;
            }
        }

        if (!CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
                compilation,
                sourceType,
                rootType,
                targetType,
                loweredExpression.Expression,
                out var expressionAnalysis,
                out errorMessage))
        {
            errorCode = "AXSG0117";
            errorMessage = $"x:Bind expression '{xBindMarkup.Path}' is invalid: {errorMessage}";
            return false;
        }

        resultTypeName = expressionAnalysis.ResultTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sourceReference = sourceConfiguration.SourceReference;
        var dependencies = xBindExpression is null
            ? ImmutableArray<XBindPathReference>.Empty
            : XBindExpressionSemanticService.CollectDependencies(xBindExpression, loweringContext)
                .Where(candidate => !XBindExpressionSemanticService.IsMainSourceReference(candidate, sourceReference))
                .OrderBy(static candidate => candidate.Kind)
                .ThenBy(static candidate => candidate.ElementName, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.RelativeSourceExpression, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.SourceExpression, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.Path, StringComparer.Ordinal)
                .ToImmutableArray();

        if (!TryBuildXBindOptionExpression(
                compilation,
                document,
                "Converter",
                xBindMarkup.Converter,
                targetType,
                out var converterExpression,
                out errorMessage) ||
            !TryBuildXBindOptionExpression(
                compilation,
                document,
                "ConverterCulture",
                xBindMarkup.ConverterCulture,
                targetType,
                out var converterCultureExpression,
                out errorMessage) ||
            !TryBuildXBindOptionExpression(
                compilation,
                document,
                "ConverterParameter",
                xBindMarkup.ConverterParameter,
                targetType,
                out var converterParameterExpression,
                out errorMessage) ||
            !TryBuildXBindOptionExpression(
                compilation,
                document,
                "StringFormat",
                xBindMarkup.StringFormat,
                targetType,
                out var stringFormatExpression,
                out errorMessage) ||
            !TryBuildXBindOptionExpression(
                compilation,
                document,
                "FallbackValue",
                xBindMarkup.FallbackValue,
                targetType,
                out var fallbackValueExpression,
                out errorMessage) ||
            !TryBuildXBindOptionExpression(
                compilation,
                document,
                "TargetNullValue",
                xBindMarkup.TargetNullValue,
                targetType,
                out var targetNullValueExpression,
                out errorMessage) ||
            !TryBuildXBindDelayExpression(
                compilation,
                document,
                xBindMarkup.Delay,
                targetType,
                out var delayExpression,
                out errorMessage) ||
            !TryBuildXBindUpdateSourceTriggerExpression(
                compilation,
                document,
                xBindMarkup.UpdateSourceTrigger,
                targetType,
                out var updateSourceTriggerExpression,
                out errorMessage) ||
            !TryBuildXBindPriorityExpression(
                compilation,
                document,
                xBindMarkup.Priority,
                bindingPriorityScope,
                targetType,
                out var priorityExpression,
                out errorMessage))
        {
            errorCode = "AXSG0117";
            errorMessage = $"x:Bind option is invalid: {errorMessage}";
            return false;
        }

        var bindBackExpression = "null";
        var bindBackValueTypeExpression = "null";
        if (string.Equals(effectiveMode, "TwoWay", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryBuildXBindBindBackExpression(
                    compilation,
                    document,
                    xBindExpression,
                    xBindMarkup.BindBack,
                    loweringContext,
                    sourceType,
                    rootType,
                    targetType,
                    bindingValueType,
                    expressionAnalysis.ResultTypeSymbol,
                    out bindBackExpression,
                    out bindBackValueTypeExpression,
                    out errorMessage))
            {
                errorCode = "AXSG0118";
                return false;
            }
        }

        bindingExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<" +
            GetTypeNameOrObject(sourceType, compilation) +
            ", " +
            GetTypeNameOrObject(rootType, compilation) +
            ", " +
            GetTypeNameOrObject(targetType, compilation) +
            ">(static (source, root, target) => (object?)(" +
            expressionAnalysis.NormalizedExpression +
            "), " +
            XBindExpressionSemanticService.BuildPathReferenceExpression(sourceReference) +
            ", " +
            XBindExpressionSemanticService.BuildPathReferenceArrayLiteral(dependencies) +
            ", " +
            bindingModeExpression +
            ", " +
            bindBackExpression +
            ", " +
            bindBackValueTypeExpression +
            ", " +
            converterExpression +
            ", " +
            converterCultureExpression +
            ", " +
            converterParameterExpression +
            ", " +
            stringFormatExpression +
            ", " +
            fallbackValueExpression +
            ", " +
            targetNullValueExpression +
            ", " +
            delayExpression +
            ", " +
            updateSourceTriggerExpression +
            ", " +
            priorityExpression +
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

    private static bool IsXBindNameScopeBoundary(XamlObjectNode node)
    {
        return IsKnownTemplateKind(node.XmlTypeName);
    }
}
