using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private enum XBindSourceReferenceKind
    {
        DataContext = 0,
        Root = 1,
        Target = 2,
        ElementName = 3,
        TemplatedParent = 4,
        FindAncestor = 5,
        ExplicitSource = 6
    }

    private readonly record struct XBindPathReference(
        XBindSourceReferenceKind Kind,
        string Path,
        string? ElementName,
        string? RelativeSourceExpression,
        string? SourceExpression);

    private readonly record struct XBindSourceConfiguration(
        INamedTypeSymbol SourceType,
        XBindPathReference SourceReference);

    private readonly record struct XBindLoweredExpression(
        string Expression,
        bool IsTypeReference);

    private sealed class XBindLoweringContext
    {
        public XBindLoweringContext(
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol sourceType,
            INamedTypeSymbol rootType,
            INamedTypeSymbol? targetType,
            XBindPathReference defaultSourceReference)
        {
            Compilation = compilation;
            Document = document;
            SourceType = sourceType;
            RootType = rootType;
            TargetType = targetType;
            DefaultSourceReference = defaultSourceReference;
        }

        public Compilation Compilation { get; }

        public XamlDocumentModel Document { get; }

        public INamedTypeSymbol SourceType { get; }

        public INamedTypeSymbol RootType { get; }

        public INamedTypeSymbol? TargetType { get; }

        public XBindPathReference DefaultSourceReference { get; }
    }

    private static bool TryBuildXBindBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        XBindMarkup xBindMarkup,
        INamedTypeSymbol? ambientSourceType,
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

        var sourceType = ambientSourceType;
        if (!string.IsNullOrWhiteSpace(xBindMarkup.DataType))
        {
            sourceType = ResolveTypeToken(compilation, document, Unquote(xBindMarkup.DataType!), document.ClassNamespace);
            if (sourceType is null)
            {
                errorCode = "AXSG0110";
                errorMessage = $"x:Bind specifies invalid DataType '{xBindMarkup.DataType}'.";
                return false;
            }
        }
        else if (!isInsideDataTemplate)
        {
            sourceType = rootType;
        }

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
                xBindMarkup,
                sourceType,
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

            if (!TryLowerXBindExpression(
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
            : CollectXBindDependencies(xBindExpression, loweringContext)
                .Where(candidate => !IsMainXBindSourceReference(candidate, sourceReference))
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
            BuildXBindPathReferenceExpression(sourceReference) +
            ", " +
            BuildXBindPathReferenceArrayLiteral(dependencies) +
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

    private static bool TryResolveXBindSourceConfiguration(
        Compilation compilation,
        XamlDocumentModel document,
        XBindMarkup xBindMarkup,
        INamedTypeSymbol baseSourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        XBindPathReference baseSourceReference,
        out XBindSourceConfiguration sourceConfiguration,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        sourceConfiguration = new XBindSourceConfiguration(baseSourceType, baseSourceReference);
        var hasExplicitDataType = !string.IsNullOrWhiteSpace(xBindMarkup.DataType);

        if (!string.IsNullOrWhiteSpace(xBindMarkup.ElementName))
        {
            if (!TryResolveXBindNamedElementType(
                    compilation,
                    document,
                    xBindMarkup.ElementName!,
                    out var namedElementType))
            {
                errorMessage = $"Named element '{xBindMarkup.ElementName}' could not be resolved.";
                return false;
            }

            sourceConfiguration = new XBindSourceConfiguration(
                hasExplicitDataType ? baseSourceType : namedElementType,
                new XBindPathReference(
                    XBindSourceReferenceKind.ElementName,
                    ".",
                    xBindMarkup.ElementName,
                    null,
                    null));
            return true;
        }

        if (xBindMarkup.RelativeSource is { } relativeSource)
        {
            if (!TryBuildRelativeSourceExpression(relativeSource, compilation, document, out var relativeSourceExpression))
            {
                errorMessage = "RelativeSource could not be converted.";
                return false;
            }

            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase))
            {
                if (targetType is null && !hasExplicitDataType)
                {
                    errorMessage = "RelativeSource Self requires a known target type or explicit DataType.";
                    return false;
                }

                sourceConfiguration = new XBindSourceConfiguration(
                    hasExplicitDataType ? baseSourceType : targetType!,
                    new XBindPathReference(
                        XBindSourceReferenceKind.Target,
                        ".",
                        null,
                        null,
                        null));
                return true;
            }

            if (string.Equals(relativeSource.Mode, "TemplatedParent", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasExplicitDataType)
                {
                    errorMessage = "RelativeSource TemplatedParent requires explicit DataType for x:Bind.";
                    return false;
                }

                sourceConfiguration = new XBindSourceConfiguration(
                    baseSourceType,
                    new XBindPathReference(
                        XBindSourceReferenceKind.TemplatedParent,
                        ".",
                        null,
                        relativeSourceExpression,
                        null));
                return true;
            }

            if (string.Equals(relativeSource.Mode, "DataContext", StringComparison.OrdinalIgnoreCase))
            {
                sourceConfiguration = new XBindSourceConfiguration(
                    baseSourceType,
                    new XBindPathReference(
                        XBindSourceReferenceKind.DataContext,
                        ".",
                        null,
                        null,
                        null));
                return true;
            }

            if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
            {
                var ancestorType = ResolveTypeToken(
                    compilation,
                    document,
                    relativeSource.AncestorTypeToken!,
                    document.ClassNamespace);
                if (ancestorType is null && !hasExplicitDataType)
                {
                    errorMessage = $"Ancestor type '{relativeSource.AncestorTypeToken}' could not be resolved.";
                    return false;
                }

                sourceConfiguration = new XBindSourceConfiguration(
                    hasExplicitDataType ? baseSourceType : ancestorType!,
                    new XBindPathReference(
                        XBindSourceReferenceKind.FindAncestor,
                        ".",
                        null,
                        relativeSourceExpression,
                        null));
                return true;
            }

            if (!hasExplicitDataType)
            {
                errorMessage = $"RelativeSource mode '{relativeSource.Mode}' requires explicit DataType for x:Bind.";
                return false;
            }

            sourceConfiguration = new XBindSourceConfiguration(
                baseSourceType,
                new XBindPathReference(
                    XBindSourceReferenceKind.FindAncestor,
                    ".",
                    null,
                    relativeSourceExpression,
                    null));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(xBindMarkup.Source))
        {
            if (TryExtractReferenceElementName(xBindMarkup.Source, out var referenceElementName))
            {
                if (!TryResolveXBindNamedElementType(
                        compilation,
                        document,
                        referenceElementName,
                        out var referenceElementType))
                {
                    errorMessage = $"Named source reference '{referenceElementName}' could not be resolved.";
                    return false;
                }

                sourceConfiguration = new XBindSourceConfiguration(
                    hasExplicitDataType ? baseSourceType : referenceElementType,
                    new XBindPathReference(
                        XBindSourceReferenceKind.ElementName,
                        ".",
                        referenceElementName,
                        null,
                        null));
                return true;
            }

            if (!TryBuildXBindExplicitSourceExpression(
                    compilation,
                    document,
                    xBindMarkup.Source,
                    targetType,
                    out var sourceExpression,
                    out errorMessage))
            {
                return false;
            }

            var sourceType = baseSourceType;
            if (!hasExplicitDataType &&
                !TryInferXBindSourceTypeFromExpression(
                    compilation,
                    rootType,
                    targetType,
                    sourceExpression,
                    out sourceType,
                    out errorMessage))
            {
                return false;
            }

            sourceConfiguration = new XBindSourceConfiguration(
                sourceType,
                new XBindPathReference(
                    XBindSourceReferenceKind.ExplicitSource,
                    ".",
                    null,
                    null,
                    sourceExpression));
        }

        return true;
    }

    private static bool TryBuildXBindExplicitSourceExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawSource,
        INamedTypeSymbol? targetType,
        out string sourceExpression,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        sourceExpression = string.Empty;
        if (string.IsNullOrWhiteSpace(rawSource))
        {
            errorMessage = "Source must not be empty.";
            return false;
        }

        if (!TryConvertMarkupOptionValueExpression(
                rawSource,
                compilation.ObjectType,
                compilation,
                document,
                targetType,
                BindingPriorityScope.None,
                out sourceExpression))
        {
            errorMessage = "Source could not be converted.";
            return false;
        }

        return true;
    }

    private static bool TryInferXBindSourceTypeFromExpression(
        Compilation compilation,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        string sourceExpression,
        out INamedTypeSymbol sourceType,
        out string errorMessage)
    {
        sourceType = null!;
        errorMessage = string.Empty;
        if (!CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
                compilation,
                rootType,
                rootType,
                targetType,
                sourceExpression,
                out var expressionAnalysis,
                out errorMessage))
        {
            errorMessage = "Source requires explicit DataType because its type could not be inferred. " + errorMessage;
            return false;
        }

        if (expressionAnalysis.ResultTypeSymbol is not INamedTypeSymbol namedType)
        {
            errorMessage = "Source requires explicit DataType because its resolved type is not a named CLR type.";
            return false;
        }

        sourceType = namedType;
        return true;
    }

    private static bool TryBuildXBindDelayExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            expression = "0";
            errorMessage = string.Empty;
            return true;
        }

        return TryBuildXBindOptionExpression(
            compilation,
            document,
            "Delay",
            rawValue,
            setterTargetType,
            out expression,
            out errorMessage);
    }

    private static bool TryBuildXBindUpdateSourceTriggerExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            expression = "global::Avalonia.Data.UpdateSourceTrigger.Default";
            errorMessage = string.Empty;
            return true;
        }

        return TryBuildXBindOptionExpression(
            compilation,
            document,
            "UpdateSourceTrigger",
            rawValue,
            setterTargetType,
            out expression,
            out errorMessage);
    }

    private static bool TryBuildXBindEventBindingDefinition(
        Compilation compilation,
        XamlDocumentModel document,
        XBindMarkup xBindMarkup,
        string eventName,
        INamedTypeSymbol? ambientSourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol targetType,
        ITypeSymbol eventHandlerType,
        bool isInsideDataTemplate,
        int line,
        int column,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out string errorMessage)
    {
        eventBindingDefinition = null;
        errorMessage = string.Empty;

        if (rootType is null)
        {
            errorMessage = $"x:Bind event '{eventName}' requires an x:Class-backed root type.";
            return false;
        }

        if (eventHandlerType is not INamedTypeSymbol namedDelegateType ||
            !TryBuildEventBindingDelegateSignature(
                namedDelegateType,
                out var delegateTypeName,
                out var delegateParameters))
        {
            errorMessage =
                $"x:Bind event '{eventName}' is not supported for delegate type '{eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(xBindMarkup.Path))
        {
            errorMessage = $"x:Bind event '{eventName}' requires a method path.";
            return false;
        }

        var sourceType = rootType;
        if (isInsideDataTemplate)
        {
            sourceType = ambientSourceType;
            if (!string.IsNullOrWhiteSpace(xBindMarkup.DataType))
            {
                sourceType = ResolveTypeToken(compilation, document, Unquote(xBindMarkup.DataType!), document.ClassNamespace);
                if (sourceType is null)
                {
                    errorMessage = $"x:Bind event '{eventName}' specifies invalid DataType '{xBindMarkup.DataType}'.";
                    return false;
                }
            }
        }

        if (sourceType is null)
        {
            errorMessage = $"x:Bind event '{eventName}' requires x:DataType in scope when used inside a DataTemplate.";
            return false;
        }

        if (xBindMarkup.HasSourceConflict)
        {
            errorMessage = xBindMarkup.SourceConflictMessage ?? "x:Bind source configuration is invalid.";
            return false;
        }

        if (!XBindExpressionParser.TryParse(xBindMarkup.Path, out var xBindExpression, out var parseError) ||
            xBindExpression is null)
        {
            errorMessage = $"x:Bind event '{eventName}' expression '{xBindMarkup.Path}' is invalid: {parseError}";
            return false;
        }

        var baseSourceReference = isInsideDataTemplate
            ? new XBindPathReference(XBindSourceReferenceKind.DataContext, ".", null, null, null)
            : new XBindPathReference(XBindSourceReferenceKind.Root, ".", null, null, null);
        if (!TryResolveXBindSourceConfiguration(
                compilation,
                document,
                xBindMarkup,
                sourceType,
                rootType,
                targetType,
                baseSourceReference,
                out var sourceConfiguration,
                out errorMessage))
        {
            errorMessage = $"x:Bind event '{eventName}' source is invalid: {errorMessage}";
            return false;
        }

        var loweringContext = new XBindLoweringContext(
            compilation,
            document,
            sourceConfiguration.SourceType,
            rootType,
            targetType,
            sourceConfiguration.SourceReference);

        if (!TryLowerXBindExpression(
                xBindExpression,
                loweringContext,
                out var loweredTarget,
                out errorMessage))
        {
            errorMessage = $"x:Bind event '{eventName}' expression '{xBindMarkup.Path}' is invalid: {errorMessage}";
            return false;
        }

        if (loweredTarget.IsTypeReference)
        {
            errorMessage = $"x:Bind event '{eventName}' expression '{xBindMarkup.Path}' must resolve to a callable member.";
            return false;
        }

        if (namedDelegateType.DelegateInvokeMethod is not IMethodSymbol invokeMethod)
        {
            errorMessage = $"x:Bind event '{eventName}' delegate type '{delegateTypeName}' does not expose Invoke.";
            return false;
        }

        var lambdaParameterNames = invokeMethod.Parameters
            .Select(static (parameter, index) => "__arg" + index.ToString(CultureInfo.InvariantCulture))
            .ToImmutableArray();
        var lambdaParameterList = string.Join(", ", lambdaParameterNames);
        string? analyzedLambdaExpression = null;
        string? lastCandidateError = null;

        foreach (var candidateBody in BuildXBindEventCandidateBodies(xBindExpression, loweredTarget.Expression, lambdaParameterNames))
        {
            var candidateLambda = "(" + lambdaParameterList + ") => " + candidateBody;
            if (CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                    compilation,
                    sourceConfiguration.SourceType,
                    rootType,
                    targetType,
                    namedDelegateType,
                    candidateLambda,
                    out var lambdaAnalysis,
                    out _))
            {
                analyzedLambdaExpression = lambdaAnalysis.RewrittenLambdaExpression;
                break;
            }

            if (CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                    compilation,
                    sourceConfiguration.SourceType,
                    rootType,
                    targetType,
                    namedDelegateType,
                    candidateLambda,
                    out _,
                    out var candidateError))
            {
                continue;
            }

            lastCandidateError = candidateError;
        }

        if (string.IsNullOrWhiteSpace(analyzedLambdaExpression))
        {
            errorMessage = string.IsNullOrWhiteSpace(lastCandidateError)
                ? $"x:Bind event '{eventName}' expression '{xBindMarkup.Path}' is not compatible with delegate '{delegateTypeName}'."
                : $"x:Bind event '{eventName}' expression '{xBindMarkup.Path}' is not compatible with delegate '{delegateTypeName}': {lastCandidateError}";
            return false;
        }

        var sourceTypeName = sourceConfiguration.SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rootTypeName = rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var methodName = BuildGeneratedEventBindingMethodName(
            eventName,
            BuildInlineEventBindingStableKey(
                analyzedLambdaExpression!,
                namedDelegateType,
                isInsideDataTemplate ? sourceConfiguration.SourceType : null,
                rootType,
                targetType,
                isLambdaExpression: true));

        eventBindingDefinition = new ResolvedEventBindingDefinition(
            GeneratedMethodName: methodName,
            DelegateTypeName: delegateTypeName,
            Parameters: delegateParameters,
            TargetKind: ResolvedEventBindingTargetKind.Lambda,
            SourceMode: ResolvedEventBindingSourceMode.Root,
            TargetPath: xBindMarkup.Path,
            ParameterPath: null,
            ParameterValueExpression: null,
            HasParameterValueExpression: false,
            PassEventArgs: false,
            DataContextTypeName: null,
            RootTypeName: rootTypeName,
            CompiledDataContextTargetPath: null,
            CompiledRootTargetPath: null,
            CompiledDataContextMethodCall: null,
            CompiledRootMethodCall: null,
            CompiledDataContextLambdaExpression: null,
            CompiledRootLambdaExpression: analyzedLambdaExpression,
            CompiledDataContextParameterPath: null,
            CompiledRootParameterPath: null,
            LambdaSourceTypeName: sourceTypeName,
            LambdaSourceDependencyExpression: BuildXBindPathReferenceExpression(sourceConfiguration.SourceReference),
            LambdaContextTargetTypeName: targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            UsesInlineCodeContext: true,
            Line: line,
            Column: column);
        return true;
    }

    private static IEnumerable<string> BuildXBindEventCandidateBodies(
        XBindExpressionNode xBindExpression,
        string loweredTargetExpression,
        ImmutableArray<string> lambdaParameterNames)
    {
        if (xBindExpression is XBindInvocationExpression)
        {
            yield return loweredTargetExpression;
            yield break;
        }

        for (var parameterCount = 0; parameterCount <= lambdaParameterNames.Length; parameterCount++)
        {
            yield return loweredTargetExpression + "(" +
                         string.Join(", ", lambdaParameterNames.Take(parameterCount)) +
                         ")";
        }
    }

    private static bool IsMainXBindSourceReference(XBindPathReference candidate, XBindPathReference sourceReference)
    {
        return candidate.Kind == sourceReference.Kind &&
               string.Equals(candidate.Path, sourceReference.Path, StringComparison.Ordinal) &&
               string.Equals(candidate.ElementName, sourceReference.ElementName, StringComparison.Ordinal) &&
               string.Equals(candidate.RelativeSourceExpression, sourceReference.RelativeSourceExpression, StringComparison.Ordinal) &&
               string.Equals(candidate.SourceExpression, sourceReference.SourceExpression, StringComparison.Ordinal);
    }

    private static bool TryBuildXBindBindBackExpression(
        Compilation compilation,
        XamlDocumentModel document,
        XBindExpressionNode? xBindExpression,
        string? explicitBindBack,
        XBindLoweringContext loweringContext,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        ITypeSymbol? bindingValueType,
        ITypeSymbol? expressionResultType,
        out string bindBackExpression,
        out string bindBackValueTypeExpression,
        out string errorMessage)
    {
        bindBackExpression = "null";
        bindBackValueTypeExpression = "null";
        errorMessage = string.Empty;

        var bindBackValueType = bindingValueType ?? expressionResultType ?? compilation.ObjectType;
        bindBackValueTypeExpression = "typeof(" +
                                      bindBackValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                      ")";
        var coercedValueExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<" +
            bindBackValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            ">(value)";

        string candidateLambda;
        if (!string.IsNullOrWhiteSpace(explicitBindBack))
        {
            if (!XBindExpressionParser.TryParse(explicitBindBack!, out var bindBackNode, out var bindBackParseError) ||
                bindBackNode is null)
            {
                errorMessage = $"BindBack expression '{explicitBindBack}' is invalid: {bindBackParseError}";
                return false;
            }

            if (!TryLowerXBindExpression(bindBackNode, loweringContext, out var bindBackTarget, out errorMessage))
            {
                errorMessage = $"BindBack expression '{explicitBindBack}' is invalid: {errorMessage}";
                return false;
            }

            if (bindBackTarget.IsTypeReference)
            {
                errorMessage = $"BindBack expression '{explicitBindBack}' must resolve to an invocable member.";
                return false;
            }

            candidateLambda = "static (source, value) => " + bindBackTarget.Expression + "(" + coercedValueExpression + ")";
        }
        else
        {
            if (xBindExpression is null)
            {
                errorMessage = "TwoWay x:Bind requires an assignable path or an explicit BindBack method.";
                return false;
            }

            if (!TryBuildXBindAssignmentExpression(
                    xBindExpression,
                    loweringContext,
                    expressionResultType ?? bindBackValueType,
                    coercedValueExpression,
                    out var assignmentExpression,
                    out errorMessage))
            {
                errorMessage = "TwoWay x:Bind requires an assignable path or an explicit BindBack method. " + errorMessage;
                return false;
            }

            candidateLambda = "static (source, value) => " + assignmentExpression;
        }

        if (!TryAnalyzeXBindBindBackLambda(
                compilation,
                sourceType,
                rootType,
                targetType,
                candidateLambda,
                out var analyzedLambda,
                out errorMessage))
        {
            errorMessage = $"TwoWay x:Bind bind-back is invalid: {errorMessage}";
            return false;
        }

        bindBackExpression = analyzedLambda;
        return true;
    }

    private static bool TryAnalyzeXBindBindBackLambda(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        string lambdaExpression,
        out string analyzedLambdaExpression,
        out string errorMessage)
    {
        analyzedLambdaExpression = string.Empty;
        errorMessage = string.Empty;

        var actionType = GetActiveTypeSymbolCatalog(compilation).GetOrDefault(TypeContractId.SystemActionOfT1T2);
        if (actionType is null)
        {
            errorMessage = "System.Action<T1, T2> could not be resolved.";
            return false;
        }

        var delegateType = actionType.Construct(sourceType, compilation.GetSpecialType(SpecialType.System_Object));
        if (!CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                compilation,
                sourceType,
                rootType,
                targetType,
                delegateType,
                lambdaExpression,
                out var lambdaAnalysis,
                out errorMessage))
        {
            return false;
        }

        analyzedLambdaExpression = lambdaAnalysis.RewrittenLambdaExpression;
        return true;
    }

    private static bool TryBuildXBindAssignmentExpression(
        XBindExpressionNode node,
        XBindLoweringContext loweringContext,
        ITypeSymbol assignmentType,
        string valueExpression,
        out string assignmentExpression,
        out string errorMessage)
    {
        assignmentExpression = string.Empty;
        errorMessage = string.Empty;

        switch (node)
        {
            case XBindIdentifierExpression identifier:
                if (!TryLowerXBindExpression(identifier, loweringContext, out var loweredIdentifier, out errorMessage) ||
                    loweredIdentifier.IsTypeReference)
                {
                    return false;
                }

                assignmentExpression = loweredIdentifier.Expression + " = " + valueExpression;
                return true;

            case XBindMemberAccessExpression memberAccess when !memberAccess.IsConditional:
                if (!TryLowerXBindExpression(memberAccess, loweringContext, out var loweredMemberAccess, out errorMessage) ||
                    loweredMemberAccess.IsTypeReference)
                {
                    return false;
                }

                assignmentExpression = loweredMemberAccess.Expression + " = " + valueExpression;
                return true;

            case XBindIndexerExpression indexer:
                if (!TryLowerXBindExpression(indexer, loweringContext, out var loweredIndexer, out errorMessage) ||
                    loweredIndexer.IsTypeReference)
                {
                    return false;
                }

                assignmentExpression = loweredIndexer.Expression + " = " + valueExpression;
                return true;

            case XBindAttachedPropertyAccessExpression attachedProperty:
                if (!TryLowerXBindExpression(attachedProperty.Target, loweringContext, out var loweredTarget, out errorMessage))
                {
                    return false;
                }

                var ownerType = ResolveTypeToken(
                    loweringContext.Compilation,
                    loweringContext.Document,
                    attachedProperty.OwnerTypeToken,
                    loweringContext.Document.ClassNamespace);
                if (ownerType is null)
                {
                    errorMessage = $"Attached property owner type '{attachedProperty.OwnerTypeToken}' could not be resolved.";
                    return false;
                }

                assignmentExpression =
                    ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    ".Set" +
                    attachedProperty.PropertyName +
                    "(" +
                    loweredTarget.Expression +
                    ", " +
                    valueExpression +
                    ")";
                return true;

            default:
                errorMessage = "the expression is not assignable";
                return false;
        }
    }

    private static bool TryBuildXBindPriorityExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawPriorityToken,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? setterTargetType,
        out string priorityExpression,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var effectivePriorityToken = !string.IsNullOrWhiteSpace(rawPriorityToken)
            ? rawPriorityToken
            : GetDefaultBindingPriorityToken(bindingPriorityScope);

        if (string.IsNullOrWhiteSpace(effectivePriorityToken))
        {
            priorityExpression = "global::Avalonia.Data.BindingPriority.LocalValue";
            return true;
        }

        var bindingType = ResolveContractType(compilation, TypeContractId.AvaloniaBinding);
        if (bindingType is not null &&
            TryGetWritableProperty(bindingType, "Priority", out var priorityProperty) &&
            TryConvertMarkupOptionValueExpression(
                effectivePriorityToken,
                priorityProperty.Type,
                compilation,
                document,
                setterTargetType,
                BindingPriorityScope.None,
                out priorityExpression))
        {
            return true;
        }

        priorityExpression = "global::Avalonia.Data.BindingPriority.LocalValue";
        return true;
    }

    private static bool TryBuildXBindOptionExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string bindingPropertyName,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        expression = "null";
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        var bindingType = ResolveContractType(compilation, TypeContractId.AvaloniaBinding);
        if (bindingType is null ||
            !TryGetWritableProperty(bindingType, bindingPropertyName, out var property) ||
            !TryConvertMarkupOptionValueExpression(
                rawValue,
                property.Type,
                compilation,
                document,
                setterTargetType,
                BindingPriorityScope.None,
                out expression))
        {
            errorMessage = $"Could not convert x:Bind option '{bindingPropertyName}'.";
            return false;
        }

        return true;
    }

    private static string BuildXBindPathReferenceArrayLiteral(ImmutableArray<XBindPathReference> references)
    {
        if (references.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency>()";
        }

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency[] { " +
               string.Join(", ", references.Select(BuildXBindPathReferenceExpression)) +
               " }";
    }

    private static string BuildXBindPathReferenceExpression(XBindPathReference reference)
    {
        var sourceKindExpression = reference.Kind switch
        {
            XBindSourceReferenceKind.Root => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.Root",
            XBindSourceReferenceKind.Target => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.Target",
            XBindSourceReferenceKind.ElementName => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.ElementName",
            XBindSourceReferenceKind.TemplatedParent => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.TemplatedParent",
            XBindSourceReferenceKind.FindAncestor => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.FindAncestor",
            XBindSourceReferenceKind.ExplicitSource => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.ExplicitSource",
            _ => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.DataContext"
        };

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency(" +
               sourceKindExpression +
               ", " +
               "\"" + Escape(reference.Path) + "\"" +
               ", " +
               (reference.ElementName is null ? "null" : "\"" + Escape(reference.ElementName) + "\"") +
               ", " +
               (reference.RelativeSourceExpression ?? "null") +
               ", " +
               (reference.SourceExpression ?? "null") +
               ")";
    }

    private static ImmutableHashSet<XBindPathReference> CollectXBindDependencies(
        XBindExpressionNode node,
        XBindLoweringContext loweringContext)
    {
        var builder = ImmutableHashSet.CreateBuilder<XBindPathReference>();
        CollectXBindDependencies(node, loweringContext, builder);
        return builder.ToImmutable();
    }

    private static void CollectXBindDependencies(
        XBindExpressionNode node,
        XBindLoweringContext loweringContext,
        ImmutableHashSet<XBindPathReference>.Builder builder)
    {
        if (TryGetXBindPathReference(node, loweringContext, out var reference))
        {
            builder.Add(reference);
        }

        switch (node)
        {
            case XBindCastExpression { Operand: { } operand }:
                CollectXBindDependencies(operand, loweringContext, builder);
                break;
            case XBindMemberAccessExpression memberAccess:
                CollectXBindDependencies(memberAccess.Target, loweringContext, builder);
                break;
            case XBindAttachedPropertyAccessExpression attachedProperty:
                CollectXBindDependencies(attachedProperty.Target, loweringContext, builder);
                break;
            case XBindIndexerExpression indexer:
                CollectXBindDependencies(indexer.Target, loweringContext, builder);
                foreach (var argument in indexer.Arguments)
                {
                    CollectXBindDependencies(argument, loweringContext, builder);
                }
                break;
            case XBindInvocationExpression invocation:
                CollectXBindDependencies(invocation.Target, loweringContext, builder);
                foreach (var argument in invocation.Arguments)
                {
                    CollectXBindDependencies(argument, loweringContext, builder);
                }
                break;
        }
    }

    private static bool TryGetXBindPathReference(
        XBindExpressionNode node,
        XBindLoweringContext loweringContext,
        out XBindPathReference reference)
    {
        switch (node)
        {
            case XBindIdentifierExpression identifier:
                return TryResolveXBindIdentifierPathReference(identifier.Identifier, loweringContext, out reference);

            case XBindCastExpression { Operand: { } operand }:
                return TryGetXBindPathReference(operand, loweringContext, out reference);

            case XBindMemberAccessExpression memberAccess when
                TryGetXBindPathReference(memberAccess.Target, loweringContext, out var targetReference):
                reference = targetReference with
                {
                    Path = AppendXBindPathSegment(targetReference.Path, memberAccess.MemberName)
                };
                return true;

            case XBindAttachedPropertyAccessExpression attachedProperty when
                TryGetXBindPathReference(attachedProperty.Target, loweringContext, out var attachedTargetReference):
                reference = attachedTargetReference with
                {
                    Path = AppendXBindAttachedPropertyPath(
                        attachedTargetReference.Path,
                        attachedProperty.OwnerTypeToken,
                        attachedProperty.PropertyName)
                };
                return true;

            case XBindIndexerExpression indexer when
                TryGetXBindPathReference(indexer.Target, loweringContext, out var indexerTargetReference) &&
                TryRenderXBindIndexerArguments(indexer.Arguments, out var indexerSuffix):
                reference = indexerTargetReference with
                {
                    Path = indexerTargetReference.Path + indexerSuffix
                };
                return true;
        }

        reference = default;
        return false;
    }

    private static string AppendXBindPathSegment(string existingPath, string memberName)
    {
        if (string.IsNullOrWhiteSpace(existingPath) || existingPath == ".")
        {
            return memberName;
        }

        return existingPath + "." + memberName;
    }

    private static string AppendXBindAttachedPropertyPath(
        string existingPath,
        string ownerTypeToken,
        string propertyName)
    {
        var attachedSegment = "(" + ownerTypeToken + "." + propertyName + ")";
        if (string.IsNullOrWhiteSpace(existingPath) || existingPath == ".")
        {
            return attachedSegment;
        }

        return existingPath + "." + attachedSegment;
    }

    private static bool TryRenderXBindIndexerArguments(
        ImmutableArray<XBindExpressionNode> arguments,
        out string suffix)
    {
        suffix = string.Empty;
        if (arguments.IsDefaultOrEmpty)
        {
            return false;
        }

        var parts = new string[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!TryRenderXBindIndexerArgument(arguments[index], out parts[index]))
            {
                return false;
            }
        }

        suffix = "[" + string.Join(", ", parts) + "]";
        return true;
    }

    private static bool TryRenderXBindIndexerArgument(
        XBindExpressionNode argument,
        out string renderedArgument)
    {
        renderedArgument = string.Empty;
        switch (argument)
        {
            case XBindLiteralExpression literal when literal.Kind == XBindLiteralKind.String:
                renderedArgument = "\"" + Escape(literal.RawValue) + "\"";
                return true;
            case XBindLiteralExpression literal:
                renderedArgument = literal.RawValue;
                return true;
        }

        return false;
    }

    private static bool TryResolveXBindIdentifierPathReference(
        string identifier,
        XBindLoweringContext loweringContext,
        out XBindPathReference reference)
    {
        if (HasInstanceMember(loweringContext.SourceType, identifier))
        {
            reference = loweringContext.DefaultSourceReference with
            {
                Path = identifier
            };
            return true;
        }

        if (!SymbolEqualityComparer.Default.Equals(loweringContext.SourceType, loweringContext.RootType) &&
            HasInstanceMember(loweringContext.RootType, identifier))
        {
            reference = new XBindPathReference(XBindSourceReferenceKind.Root, identifier, null, null, null);
            return true;
        }

        if (loweringContext.TargetType is not null &&
            HasInstanceMember(loweringContext.TargetType, identifier))
        {
            reference = new XBindPathReference(XBindSourceReferenceKind.Target, identifier, null, null, null);
            return true;
        }

        if (TryResolveXBindNamedElementType(loweringContext.Compilation, loweringContext.Document, identifier, out _))
        {
            reference = new XBindPathReference(XBindSourceReferenceKind.ElementName, ".", identifier, null, null);
            return true;
        }

        reference = default;
        return false;
    }

    private static bool TryLowerXBindExpression(
        XBindExpressionNode node,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        switch (node)
        {
            case XBindLiteralExpression literal:
                return TryLowerXBindLiteral(literal, out loweredExpression, out errorMessage);

            case XBindIdentifierExpression identifier:
                return TryLowerXBindIdentifier(identifier, loweringContext, out loweredExpression, out errorMessage);

            case XBindCastExpression castExpression:
                return TryLowerXBindCastExpression(castExpression, loweringContext, out loweredExpression, out errorMessage);

            case XBindMemberAccessExpression memberAccess:
                return TryLowerXBindMemberAccess(memberAccess, loweringContext, out loweredExpression, out errorMessage);

            case XBindAttachedPropertyAccessExpression attachedPropertyAccess:
                return TryLowerXBindAttachedPropertyAccess(attachedPropertyAccess, loweringContext, out loweredExpression, out errorMessage);

            case XBindIndexerExpression indexerExpression:
                return TryLowerXBindIndexer(indexerExpression, loweringContext, out loweredExpression, out errorMessage);

            case XBindInvocationExpression invocationExpression:
                return TryLowerXBindInvocation(invocationExpression, loweringContext, out loweredExpression, out errorMessage);

            default:
                loweredExpression = default;
                errorMessage = "unsupported x:Bind expression node";
                return false;
        }
    }

    private static bool TryLowerXBindLiteral(
        XBindLiteralExpression literal,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        loweredExpression = literal.Kind switch
        {
            XBindLiteralKind.Null => new XBindLoweredExpression("null", IsTypeReference: false),
            XBindLiteralKind.Boolean => new XBindLoweredExpression(
                literal.RawValue.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
                IsTypeReference: false),
            XBindLiteralKind.Number => new XBindLoweredExpression(literal.RawValue, IsTypeReference: false),
            XBindLiteralKind.String => new XBindLoweredExpression("\"" + Escape(literal.RawValue) + "\"", IsTypeReference: false),
            _ => default
        };

        if (string.IsNullOrWhiteSpace(loweredExpression.Expression))
        {
            errorMessage = "unsupported x:Bind literal";
            return false;
        }

        return true;
    }

    private static bool TryLowerXBindIdentifier(
        XBindIdentifierExpression identifier,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var identifierToken = identifier.Identifier.Trim();

        if (HasInstanceMember(loweringContext.SourceType, identifierToken))
        {
            loweredExpression = new XBindLoweredExpression("source." + identifierToken, IsTypeReference: false);
            return true;
        }

        if (!SymbolEqualityComparer.Default.Equals(loweringContext.SourceType, loweringContext.RootType) &&
            HasInstanceMember(loweringContext.RootType, identifierToken))
        {
            loweredExpression = new XBindLoweredExpression("root." + identifierToken, IsTypeReference: false);
            return true;
        }

        if (loweringContext.TargetType is not null &&
            HasInstanceMember(loweringContext.TargetType, identifierToken))
        {
            loweredExpression = new XBindLoweredExpression("target." + identifierToken, IsTypeReference: false);
            return true;
        }

        if (TryResolveXBindNamedElementType(loweringContext, identifierToken, out var namedElementType))
        {
            loweredExpression = new XBindLoweredExpression(
                "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ResolveNamedElement<" +
                namedElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">(target, root, \"" +
                Escape(identifierToken) +
                "\")",
                IsTypeReference: false);
            return true;
        }

        var resolvedType = ResolveTypeToken(
            loweringContext.Compilation,
            loweringContext.Document,
            identifierToken,
            loweringContext.Document.ClassNamespace);
        if (resolvedType is not null)
        {
            loweredExpression = new XBindLoweredExpression(
                resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsTypeReference: true);
            return true;
        }

        loweredExpression = default;
        errorMessage = $"Identifier '{identifierToken}' could not be resolved against the x:Bind source, root, target, named elements, or known types.";
        return false;
    }

    private static bool TryLowerXBindCastExpression(
        XBindCastExpression castExpression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        var resolvedType = ResolveTypeToken(
            loweringContext.Compilation,
            loweringContext.Document,
            castExpression.TypeToken,
            loweringContext.Document.ClassNamespace);
        if (resolvedType is null)
        {
            loweredExpression = default;
            errorMessage = $"Cast type '{castExpression.TypeToken}' could not be resolved.";
            return false;
        }

        var typeName = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (castExpression.Operand is null)
        {
            loweredExpression = new XBindLoweredExpression("((" + typeName + ")source)", IsTypeReference: false);
            errorMessage = string.Empty;
            return true;
        }

        if (!TryLowerXBindExpression(castExpression.Operand, loweringContext, out var loweredOperand, out errorMessage))
        {
            loweredExpression = default;
            return false;
        }

        loweredExpression = new XBindLoweredExpression(
            "((" + typeName + ")" + loweredOperand.Expression + ")",
            IsTypeReference: false);
        return true;
    }

    private static bool TryLowerXBindMemberAccess(
        XBindMemberAccessExpression memberAccess,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        if (!TryLowerXBindExpression(memberAccess.Target, loweringContext, out var loweredTarget, out errorMessage))
        {
            loweredExpression = default;
            return false;
        }

        var separator = memberAccess.IsConditional ? "?." : ".";
        loweredExpression = new XBindLoweredExpression(
            loweredTarget.Expression + separator + memberAccess.MemberName,
            IsTypeReference: false);
        return true;
    }

    private static bool TryLowerXBindAttachedPropertyAccess(
        XBindAttachedPropertyAccessExpression attachedPropertyAccess,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        if (!TryLowerXBindExpression(attachedPropertyAccess.Target, loweringContext, out var loweredTarget, out errorMessage))
        {
            loweredExpression = default;
            return false;
        }

        var ownerType = ResolveTypeToken(
            loweringContext.Compilation,
            loweringContext.Document,
            attachedPropertyAccess.OwnerTypeToken,
            loweringContext.Document.ClassNamespace);
        if (ownerType is null)
        {
            loweredExpression = default;
            errorMessage = $"Attached property owner type '{attachedPropertyAccess.OwnerTypeToken}' could not be resolved.";
            return false;
        }

        loweredExpression = new XBindLoweredExpression(
            ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            ".Get" +
            attachedPropertyAccess.PropertyName +
            "(" +
            loweredTarget.Expression +
            ")",
            IsTypeReference: false);
        return true;
    }

    private static bool TryLowerXBindIndexer(
        XBindIndexerExpression indexerExpression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        if (!TryLowerXBindExpression(indexerExpression.Target, loweringContext, out var loweredTarget, out errorMessage))
        {
            loweredExpression = default;
            return false;
        }

        var loweredArguments = new string[indexerExpression.Arguments.Length];
        for (var index = 0; index < indexerExpression.Arguments.Length; index++)
        {
            if (!TryLowerXBindExpression(indexerExpression.Arguments[index], loweringContext, out var loweredArgument, out errorMessage))
            {
                loweredExpression = default;
                return false;
            }

            loweredArguments[index] = loweredArgument.Expression;
        }

        loweredExpression = new XBindLoweredExpression(
            loweredTarget.Expression + "[" + string.Join(", ", loweredArguments) + "]",
            IsTypeReference: false);
        return true;
    }

    private static bool TryLowerXBindInvocation(
        XBindInvocationExpression invocationExpression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        if (!TryLowerXBindExpression(invocationExpression.Target, loweringContext, out var loweredTarget, out errorMessage))
        {
            loweredExpression = default;
            return false;
        }

        var loweredArguments = new string[invocationExpression.Arguments.Length];
        for (var index = 0; index < invocationExpression.Arguments.Length; index++)
        {
            if (!TryLowerXBindExpression(invocationExpression.Arguments[index], loweringContext, out var loweredArgument, out errorMessage))
            {
                loweredExpression = default;
                return false;
            }

            loweredArguments[index] = loweredArgument.Expression;
        }

        loweredExpression = new XBindLoweredExpression(
            loweredTarget.Expression + "(" + string.Join(", ", loweredArguments) + ")",
            IsTypeReference: false);
        return true;
    }

    private static bool TryResolveXBindNamedElementType(
        XBindLoweringContext loweringContext,
        string elementName,
        out INamedTypeSymbol typeSymbol)
    {
        return TryResolveXBindNamedElementType(
            loweringContext.Compilation,
            loweringContext.Document,
            elementName,
            out typeSymbol);
    }

    private static bool TryResolveXBindNamedElementType(
        Compilation compilation,
        XamlDocumentModel document,
        string elementName,
        out INamedTypeSymbol typeSymbol)
    {
        foreach (var namedElement in document.NamedElements)
        {
            if (!string.Equals(namedElement.Name, elementName, StringComparison.Ordinal))
            {
                continue;
            }

            var resolvedType = ResolveTypeSymbol(
                compilation,
                namedElement.XmlNamespace,
                namedElement.XmlTypeName);
            if (resolvedType is not null)
            {
                typeSymbol = resolvedType;
                return true;
            }
        }

        typeSymbol = null!;
        return false;
    }

    private static bool HasInstanceMember(INamedTypeSymbol? typeSymbol, string memberName)
    {
        if (typeSymbol is null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(memberName))
            {
                switch (member)
                {
                    case IPropertySymbol property when !property.IsStatic && property.GetMethod is not null:
                    case IFieldSymbol field when !field.IsStatic:
                    case IMethodSymbol method when !method.IsStatic &&
                                                   method.MethodKind == MethodKind.Ordinary &&
                                                   !method.IsImplicitlyDeclared:
                        return true;
                }
            }
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            foreach (var member in interfaceType.GetMembers(memberName))
            {
                switch (member)
                {
                    case IPropertySymbol property when property.GetMethod is not null:
                    case IFieldSymbol:
                    case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary &&
                                                   !method.IsImplicitlyDeclared:
                        return true;
                }
            }
        }

        return false;
    }
}
