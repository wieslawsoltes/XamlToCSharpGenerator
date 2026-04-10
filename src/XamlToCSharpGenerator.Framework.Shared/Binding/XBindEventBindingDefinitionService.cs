using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XBindEventBindingDefinitionService
{
    public delegate bool TryResolveExplicitXBindSourceTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawDataType,
        out INamedTypeSymbol? explicitSourceType,
        out string errorMessage);

    public delegate bool TryResolveXBindSourceConfigurationDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        XBindMarkup xBindMarkup,
        INamedTypeSymbol baseSourceType,
        INamedTypeSymbol? ambientDataContextType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        XBindPathReference baseSourceReference,
        out ResolvedXBindSourceConfiguration sourceConfiguration,
        out string errorMessage);

    public delegate bool TryBuildDelegateSignatureDelegate(
        INamedTypeSymbol delegateType,
        out string delegateTypeName,
        out ImmutableArray<ResolvedEventBindingParameter> delegateParameters);

    public delegate bool TryLowerExpressionDelegate(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage);

    public delegate IEnumerable<string> BuildEventCandidateBodiesDelegate(
        XBindExpressionNode expression,
        string loweredTargetExpression,
        ImmutableArray<string> lambdaParameterNames);

    public delegate string BuildPathReferenceExpressionDelegate(XBindPathReference sourceReference);

    public delegate string BuildGeneratedMethodNameDelegate(string eventName, string stableKey);

    public delegate string BuildInlineStableKeyDelegate(
        string normalizedExpression,
        INamedTypeSymbol delegateType,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        bool isLambdaExpression);

    private readonly TryResolveExplicitXBindSourceTypeDelegate _tryResolveExplicitXBindSourceType;
    private readonly TryResolveXBindSourceConfigurationDelegate _tryResolveSourceConfiguration;
    private readonly TryBuildDelegateSignatureDelegate _tryBuildDelegateSignature;
    private readonly TryLowerExpressionDelegate _tryLowerExpression;
    private readonly BuildEventCandidateBodiesDelegate _buildEventCandidateBodies;
    private readonly BuildPathReferenceExpressionDelegate _buildPathReferenceExpression;
    private readonly BuildGeneratedMethodNameDelegate _buildGeneratedMethodName;
    private readonly BuildInlineStableKeyDelegate _buildInlineStableKey;

    public XBindEventBindingDefinitionService(
        TryResolveExplicitXBindSourceTypeDelegate tryResolveExplicitXBindSourceType,
        TryResolveXBindSourceConfigurationDelegate tryResolveSourceConfiguration,
        TryBuildDelegateSignatureDelegate tryBuildDelegateSignature,
        TryLowerExpressionDelegate tryLowerExpression,
        BuildEventCandidateBodiesDelegate buildEventCandidateBodies,
        BuildPathReferenceExpressionDelegate buildPathReferenceExpression,
        BuildGeneratedMethodNameDelegate buildGeneratedMethodName,
        BuildInlineStableKeyDelegate buildInlineStableKey)
    {
        _tryResolveExplicitXBindSourceType = tryResolveExplicitXBindSourceType ?? throw new ArgumentNullException(nameof(tryResolveExplicitXBindSourceType));
        _tryResolveSourceConfiguration = tryResolveSourceConfiguration ?? throw new ArgumentNullException(nameof(tryResolveSourceConfiguration));
        _tryBuildDelegateSignature = tryBuildDelegateSignature ?? throw new ArgumentNullException(nameof(tryBuildDelegateSignature));
        _tryLowerExpression = tryLowerExpression ?? throw new ArgumentNullException(nameof(tryLowerExpression));
        _buildEventCandidateBodies = buildEventCandidateBodies ?? throw new ArgumentNullException(nameof(buildEventCandidateBodies));
        _buildPathReferenceExpression = buildPathReferenceExpression ?? throw new ArgumentNullException(nameof(buildPathReferenceExpression));
        _buildGeneratedMethodName = buildGeneratedMethodName ?? throw new ArgumentNullException(nameof(buildGeneratedMethodName));
        _buildInlineStableKey = buildInlineStableKey ?? throw new ArgumentNullException(nameof(buildInlineStableKey));
    }

    public bool TryBuildDefinition(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        XBindMarkup xBindMarkup,
        string eventName,
        INamedTypeSymbol? ambientDataContextType,
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
            !_tryBuildDelegateSignature(
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

        if (!_tryResolveExplicitXBindSourceType(
                compilation,
                document,
                xBindMarkup.DataType,
                out var explicitSourceType,
                out var explicitDataTypeError))
        {
            errorMessage = explicitDataTypeError;
            return false;
        }

        var sourceType = explicitSourceType ?? (isInsideDataTemplate ? ambientDataContextType : rootType);
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
        if (!_tryResolveSourceConfiguration(
                compilation,
                document,
                currentNode,
                xBindMarkup,
                sourceType,
                explicitSourceType ?? ambientDataContextType,
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
            currentNode,
            sourceConfiguration.SourceType,
            rootType,
            targetType,
            sourceConfiguration.SourceReference);

        if (!_tryLowerExpression(
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

        foreach (var candidateBody in _buildEventCandidateBodies(xBindExpression, loweredTarget.Expression, lambdaParameterNames))
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

        var methodName = _buildGeneratedMethodName(
            eventName,
            _buildInlineStableKey(
                analyzedLambdaExpression!,
                namedDelegateType,
                sourceConfiguration.SourceType,
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
            RootTypeName: rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            CompiledDataContextTargetPath: null,
            CompiledRootTargetPath: null,
            CompiledDataContextMethodCall: null,
            CompiledRootMethodCall: null,
            CompiledDataContextLambdaExpression: null,
            CompiledRootLambdaExpression: analyzedLambdaExpression,
            CompiledDataContextParameterPath: null,
            CompiledRootParameterPath: null,
            LambdaSourceTypeName: sourceConfiguration.SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            LambdaSourceDependencyExpression: _buildPathReferenceExpression(sourceConfiguration.SourceReference),
            LambdaContextTargetTypeName: targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            UsesInlineCodeContext: true,
            Line: line,
            Column: column);
        return true;
    }
}
