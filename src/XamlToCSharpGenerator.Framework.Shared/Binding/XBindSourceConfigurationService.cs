using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XBindSourceConfigurationService
{
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string typeToken,
        string? fallbackClrNamespace);

    public delegate bool TryResolveNamedElementTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        string elementName,
        out INamedTypeSymbol? typeSymbol);

    public delegate bool TryBuildRelativeSourceExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        RelativeSourceMarkup relativeSource,
        out string expression,
        out string errorMessage);

    public delegate bool TryExtractReferenceElementNameDelegate(string? sourceValue, out string elementName);

    public delegate bool TryBuildExplicitSourceExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string rawSource,
        out string expression,
        out string errorMessage);

    private readonly ResolveTypeTokenDelegate _resolveTypeToken;
    private readonly TryResolveNamedElementTypeDelegate _tryResolveNamedElementType;
    private readonly TryBuildRelativeSourceExpressionDelegate _tryBuildRelativeSourceExpression;
    private readonly TryExtractReferenceElementNameDelegate _tryExtractReferenceElementName;
    private readonly TryBuildExplicitSourceExpressionDelegate _tryBuildExplicitSourceExpression;

    public XBindSourceConfigurationService(
        ResolveTypeTokenDelegate resolveTypeToken,
        TryResolveNamedElementTypeDelegate tryResolveNamedElementType,
        TryBuildRelativeSourceExpressionDelegate tryBuildRelativeSourceExpression,
        TryExtractReferenceElementNameDelegate tryExtractReferenceElementName,
        TryBuildExplicitSourceExpressionDelegate tryBuildExplicitSourceExpression)
    {
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _tryResolveNamedElementType = tryResolveNamedElementType ?? throw new ArgumentNullException(nameof(tryResolveNamedElementType));
        _tryBuildRelativeSourceExpression = tryBuildRelativeSourceExpression ?? throw new ArgumentNullException(nameof(tryBuildRelativeSourceExpression));
        _tryExtractReferenceElementName = tryExtractReferenceElementName ?? throw new ArgumentNullException(nameof(tryExtractReferenceElementName));
        _tryBuildExplicitSourceExpression = tryBuildExplicitSourceExpression ?? throw new ArgumentNullException(nameof(tryBuildExplicitSourceExpression));
    }

    public bool TryResolveSourceConfiguration(
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
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var hasExplicitDataType = !string.IsNullOrWhiteSpace(xBindMarkup.DataType);

        if (!string.IsNullOrWhiteSpace(xBindMarkup.ElementName))
        {
            if (!_tryResolveNamedElementType(
                    compilation,
                    document,
                    currentNode,
                    xBindMarkup.ElementName!,
                    out var namedElementType) ||
                namedElementType is null)
            {
                sourceConfiguration = default;
                errorMessage = "Could not resolve x:Bind ElementName '" + xBindMarkup.ElementName + "'.";
                return false;
            }

            sourceConfiguration = new ResolvedXBindSourceConfiguration(
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
            string? relativeSourceExpression = null;
            string relativeSourceBuildError = string.Empty;

            bool TryGetRelativeSourceExpression(out string expression)
            {
                if (relativeSourceExpression is not null)
                {
                    expression = relativeSourceExpression;
                    return true;
                }

                if (!_tryBuildRelativeSourceExpression(
                        compilation,
                        document,
                        relativeSource,
                        out expression,
                        out relativeSourceBuildError))
                {
                    return false;
                }

                relativeSourceExpression = expression;
                return true;
            }

            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase))
            {
                if (targetType is null && !hasExplicitDataType)
                {
                    sourceConfiguration = default;
                    errorMessage = "RelativeSource Self requires a known target type or explicit DataType.";
                    return false;
                }

                sourceConfiguration = new ResolvedXBindSourceConfiguration(
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
                    sourceConfiguration = default;
                    errorMessage = "RelativeSource TemplatedParent requires explicit DataType for x:Bind.";
                    return false;
                }

                if (!TryGetRelativeSourceExpression(out var templatedParentExpression))
                {
                    sourceConfiguration = default;
                    errorMessage = string.IsNullOrWhiteSpace(relativeSourceBuildError)
                        ? "RelativeSource could not be converted."
                        : relativeSourceBuildError;
                    return false;
                }

                sourceConfiguration = new ResolvedXBindSourceConfiguration(
                    baseSourceType,
                    new XBindPathReference(
                        XBindSourceReferenceKind.TemplatedParent,
                        ".",
                        null,
                        templatedParentExpression,
                        null));
                return true;
            }

            if (string.Equals(relativeSource.Mode, "DataContext", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasExplicitDataType && ambientDataContextType is null)
                {
                    sourceConfiguration = default;
                    errorMessage = "RelativeSource DataContext requires x:DataType in scope or explicit DataType for x:Bind.";
                    return false;
                }

                sourceConfiguration = new ResolvedXBindSourceConfiguration(
                    hasExplicitDataType ? baseSourceType : ambientDataContextType!,
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
                if (!TryGetRelativeSourceExpression(out var ancestorExpression))
                {
                    sourceConfiguration = default;
                    errorMessage = string.IsNullOrWhiteSpace(relativeSourceBuildError)
                        ? "RelativeSource could not be converted."
                        : relativeSourceBuildError;
                    return false;
                }

                var ancestorType = _resolveTypeToken(
                    compilation,
                    document,
                    relativeSource.AncestorTypeToken!,
                    document.ClassNamespace);
                if (ancestorType is null && !hasExplicitDataType)
                {
                    sourceConfiguration = default;
                    errorMessage = "Ancestor type '" + relativeSource.AncestorTypeToken + "' could not be resolved.";
                    return false;
                }

                sourceConfiguration = new ResolvedXBindSourceConfiguration(
                    hasExplicitDataType ? baseSourceType : ancestorType!,
                    new XBindPathReference(
                        XBindSourceReferenceKind.FindAncestor,
                        ".",
                        null,
                        ancestorExpression,
                        null));
                return true;
            }

            if (!hasExplicitDataType)
            {
                sourceConfiguration = default;
                errorMessage = "RelativeSource mode '" + relativeSource.Mode + "' requires explicit DataType for x:Bind.";
                return false;
            }

            if (!TryGetRelativeSourceExpression(out var fallbackExpression))
            {
                sourceConfiguration = default;
                errorMessage = string.IsNullOrWhiteSpace(relativeSourceBuildError)
                    ? "RelativeSource could not be converted."
                    : relativeSourceBuildError;
                return false;
            }

            sourceConfiguration = new ResolvedXBindSourceConfiguration(
                baseSourceType,
                new XBindPathReference(
                    XBindSourceReferenceKind.FindAncestor,
                    ".",
                    null,
                    fallbackExpression,
                    null));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(xBindMarkup.Source))
        {
            if (_tryExtractReferenceElementName(xBindMarkup.Source, out var referenceElementName))
            {
                if (!_tryResolveNamedElementType(
                        compilation,
                        document,
                        currentNode,
                        referenceElementName,
                        out var namedElementType) ||
                    namedElementType is null)
                {
                    sourceConfiguration = default;
                    errorMessage = "Could not resolve x:Bind Source element reference '" + referenceElementName + "'.";
                    return false;
                }

                sourceConfiguration = new ResolvedXBindSourceConfiguration(
                    hasExplicitDataType ? baseSourceType : namedElementType,
                    new XBindPathReference(
                        XBindSourceReferenceKind.ElementName,
                        ".",
                        referenceElementName,
                        null,
                        null));
                return true;
            }

            if (!_tryBuildExplicitSourceExpression(
                    compilation,
                    document,
                    xBindMarkup.Source!,
                    out var explicitSourceExpression,
                    out errorMessage))
            {
                sourceConfiguration = default;
                return false;
            }

            string normalizedSourceExpression;
            CSharpInlineExpressionAnalysisResult sourceAnalysisResult = default;
            var explicitSourceType = baseSourceType;
            if (!hasExplicitDataType &&
                !CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
                    compilation,
                    rootType,
                    rootType,
                    targetType,
                    explicitSourceExpression,
                    out sourceAnalysisResult,
                    out errorMessage))
            {
                sourceConfiguration = default;
                errorMessage = "Source requires explicit DataType because its type could not be inferred. " + errorMessage;
                return false;
            }

            normalizedSourceExpression = explicitSourceExpression;
            if (!hasExplicitDataType)
            {
                normalizedSourceExpression = sourceAnalysisResult.NormalizedExpression;
                explicitSourceType = sourceAnalysisResult.ResultTypeSymbol as INamedTypeSymbol;
                if (explicitSourceType is null)
                {
                    sourceConfiguration = default;
                    errorMessage = "Source requires explicit DataType because its resolved type is not a named CLR type.";
                    return false;
                }
            }

            sourceConfiguration = new ResolvedXBindSourceConfiguration(
                explicitSourceType,
                new XBindPathReference(
                    XBindSourceReferenceKind.ExplicitSource,
                    ".",
                    null,
                    null,
                    normalizedSourceExpression));
            return true;
        }

        sourceConfiguration = new ResolvedXBindSourceConfiguration(baseSourceType, baseSourceReference);
        return true;
    }
}
