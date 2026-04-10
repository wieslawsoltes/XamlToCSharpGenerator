using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ResourceKeyResolutionService
{
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> _resolveTypeToken;
    private readonly TryResolveStaticMemberExpressionDelegate _tryResolveStaticMemberExpression;
    private readonly Func<string, string> _escape;

    public delegate bool TryResolveStaticMemberExpressionDelegate(
        string memberToken,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression);

    public ResourceKeyResolutionService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> resolveTypeToken,
        TryResolveStaticMemberExpressionDelegate tryResolveStaticMemberExpression,
        Func<string, string> escape)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension;
        _resolveTypeToken = resolveTypeToken;
        _tryResolveStaticMemberExpression = tryResolveStaticMemberExpression;
        _escape = escape;
    }

    public bool TryBuildResourceKeyExpression(
        string rawKey,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedResourceKeyExpression resourceKey)
    {
        resourceKey = default;
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return false;
        }

        if (_tryParseMarkupExtension(rawKey, out var markup))
        {
            return TryBuildResourceKeyExpression(markup, compilation, document, out resourceKey);
        }

        resourceKey = new ResolvedResourceKeyExpression(
            "\"" + _escape(rawKey.Trim()) + "\"",
            ResolvedResourceKeyKind.StringLiteral);
        return true;
    }

    public bool TryBuildResourceKeyExpression(
        MarkupExtensionInfo markup,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedResourceKeyExpression resourceKey)
    {
        resourceKey = default;
        switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
        {
            case XamlMarkupExtensionKind.Type:
            {
                var rawTypeToken = markup.PositionalArguments.Length > 0
                    ? markup.PositionalArguments[0]
                    : string.Empty;
                var resolvedType = string.IsNullOrWhiteSpace(rawTypeToken)
                    ? null
                    : _resolveTypeToken(compilation, document, rawTypeToken);
                if (resolvedType is null)
                {
                    return false;
                }

                resourceKey = new ResolvedResourceKeyExpression(
                    "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
                    ResolvedResourceKeyKind.TypeReference);
                return true;
            }

            case XamlMarkupExtensionKind.Static:
            {
                var rawMemberToken = markup.PositionalArguments.Length > 0
                    ? markup.PositionalArguments[0]
                    : string.Empty;
                if (!_tryResolveStaticMemberExpression(rawMemberToken, compilation, document, out var staticExpression))
                {
                    return false;
                }

                resourceKey = new ResolvedResourceKeyExpression(staticExpression, ResolvedResourceKeyKind.StaticMemberReference);
                return true;
            }

            default:
            {
                var rawValue = markup.PositionalArguments.Length > 0
                    ? markup.PositionalArguments[0]
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                if (_tryParseMarkupExtension(rawValue, out var nestedMarkup) &&
                    TryBuildResourceKeyExpression(nestedMarkup, compilation, document, out resourceKey))
                {
                    return true;
                }

                resourceKey = new ResolvedResourceKeyExpression(
                    "\"" + _escape(rawValue.Trim()) + "\"",
                    ResolvedResourceKeyKind.StringLiteral);
                return true;
            }
        }
    }
}
