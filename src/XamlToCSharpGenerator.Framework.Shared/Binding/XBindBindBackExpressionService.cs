using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XBindBindBackExpressionService
{
    public delegate INamedTypeSymbol? ResolveContractTypeDelegate(Compilation compilation, TypeContractId contractId);

    private readonly XBindExpressionSemanticService _xBindExpressionSemanticService;
    private readonly ResolveContractTypeDelegate _resolveContractType;

    public XBindBindBackExpressionService(
        XBindExpressionSemanticService xBindExpressionSemanticService,
        ResolveContractTypeDelegate resolveContractType)
    {
        _xBindExpressionSemanticService = xBindExpressionSemanticService ?? throw new ArgumentNullException(nameof(xBindExpressionSemanticService));
        _resolveContractType = resolveContractType ?? throw new ArgumentNullException(nameof(resolveContractType));
    }

    public bool TryBuildBindBackExpression(
        Compilation compilation,
        XamlDocumentModel document,
        XBindExpressionNode? xBindExpression,
        string? rawBindBack,
        XBindLoweringContext loweringContext,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        ITypeSymbol? bindingValueType,
        ITypeSymbol? resultTypeSymbol,
        out string bindBackExpression,
        out string bindBackValueTypeExpression,
        out string errorMessage)
    {
        bindBackExpression = "null";
        bindBackValueTypeExpression = "null";
        errorMessage = string.Empty;

        var effectiveValueType = bindingValueType ?? resultTypeSymbol ?? compilation.ObjectType;
        var effectiveValueTypeName = effectiveValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bindBackValueTypeExpression = "typeof(" + effectiveValueTypeName + ")";
        XBindLoweredExpression bindBackTarget = default;

        string? BuildCandidateLambda(string coercedValueExpression, out string candidateErrorMessage)
        {
            candidateErrorMessage = string.Empty;
            if (!string.IsNullOrWhiteSpace(rawBindBack))
            {
                return "static (source, value) => " + bindBackTarget.Expression + "(" + coercedValueExpression + ")";
            }

            if (xBindExpression is null)
            {
                candidateErrorMessage = "TwoWay x:Bind requires an assignable path or an explicit BindBack method.";
                return null;
            }

            if (!_xBindExpressionSemanticService.TryBuildAssignmentExpression(
                    xBindExpression,
                    loweringContext,
                    coercedValueExpression,
                    out var candidateAssignmentExpression,
                    out candidateErrorMessage))
            {
                candidateErrorMessage = "TwoWay x:Bind requires an assignable path or an explicit BindBack method. " + candidateErrorMessage;
                return null;
            }

            return "static (source, value) => " + candidateAssignmentExpression;
        }

        if (!string.IsNullOrWhiteSpace(rawBindBack))
        {
            if (!XBindExpressionParser.TryParse(rawBindBack!, out var bindBackNode, out var bindBackParseError) ||
                bindBackNode is null)
            {
                errorMessage = $"BindBack expression '{rawBindBack}' is invalid: {bindBackParseError}";
                return false;
            }

            if (!_xBindExpressionSemanticService.TryLowerExpression(
                    bindBackNode,
                    loweringContext,
                    out bindBackTarget,
                    out errorMessage))
            {
                errorMessage = $"BindBack expression '{rawBindBack}' is invalid: {errorMessage}";
                return false;
            }

            if (bindBackTarget.IsTypeReference)
            {
                errorMessage = $"BindBack expression '{rawBindBack}' must resolve to an invocable member.";
                return false;
            }
        }

        var actionType = _resolveContractType(compilation, TypeContractId.SystemActionOfT1T2);
        if (actionType is null)
        {
            errorMessage = "System.Action<T1, T2> contract type is not available.";
            return false;
        }

        var closedActionType = actionType.Construct(sourceType, compilation.ObjectType);
        var preferredLambda = BuildCandidateLambda(
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<" +
            effectiveValueTypeName +
            ">(value)",
            out errorMessage);
        if (string.IsNullOrWhiteSpace(preferredLambda))
        {
            return false;
        }

        var fallbackLambda = BuildCandidateLambda("((" + effectiveValueTypeName + ")(value))", out var fallbackBuildError);
        if (string.IsNullOrWhiteSpace(fallbackLambda))
        {
            errorMessage = fallbackBuildError;
            return false;
        }

        if (!TryAnalyzeCandidateLambda(
                compilation,
                sourceType,
                rootType,
                targetType,
                closedActionType,
                preferredLambda,
                fallbackLambda,
                out var lambdaAnalysis,
                out errorMessage))
        {
            return false;
        }

        bindBackExpression = lambdaAnalysis.RewrittenLambdaExpression;
        return true;
    }

    private static bool TryAnalyzeCandidateLambda(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol delegateType,
        string preferredLambda,
        string fallbackLambda,
        out SourceContextLambdaAnalysisResult lambdaAnalysis,
        out string errorMessage)
    {
        if (CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                compilation,
                sourceType,
                rootType,
                targetType,
                delegateType,
                preferredLambda,
                out lambdaAnalysis,
                out errorMessage))
        {
            return true;
        }

        return CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
            compilation,
            sourceType,
            rootType,
            targetType,
            delegateType,
            fallbackLambda,
            out lambdaAnalysis,
            out errorMessage);
    }
}
