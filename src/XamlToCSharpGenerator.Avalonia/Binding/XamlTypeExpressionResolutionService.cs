using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class XamlTypeExpressionResolutionService
{
    internal delegate INamedTypeSymbol? ResolveTypeTokenDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string fallbackClrNamespace);

    private readonly MarkupExpressionParser _markupExpressionParser;
    private readonly ResolveTypeTokenDelegate _resolveTypeToken;

    public XamlTypeExpressionResolutionService(
        MarkupExpressionParser markupExpressionParser,
        ResolveTypeTokenDelegate resolveTypeToken)
    {
        _markupExpressionParser = markupExpressionParser ?? throw new ArgumentNullException(nameof(markupExpressionParser));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
    }

    public INamedTypeSymbol? ResolveTypeFromExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace)
    {
        if (!TryExtractTypeToken(typeExpression, out var token))
        {
            return null;
        }

        var fallbackNamespace = string.IsNullOrWhiteSpace(fallbackClrNamespace)
            ? document.ClassNamespace
            : fallbackClrNamespace!;
        return _resolveTypeToken(compilation, document, token, fallbackNamespace);
    }

    public bool TryExtractTypeToken(string? typeExpression, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(typeExpression))
        {
            return false;
        }

        var trimmed = typeExpression.Trim();
        if (!_markupExpressionParser.TryParseMarkupExtension(trimmed, out var markup) ||
            !IsTypeMarkupExtension(markup.Name))
        {
            token = trimmed;
            return token.Length > 0;
        }

        var typeToken = ResolveTypeMarkupPayload(markup);
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            return false;
        }

        token = Unquote(typeToken);
        return token.Length > 0;
    }

    private static bool IsTypeMarkupExtension(string extensionName)
    {
        return extensionName.Equals("x:Type", StringComparison.OrdinalIgnoreCase) ||
               extensionName.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
               extensionName.Equals("x:TypeExtension", StringComparison.OrdinalIgnoreCase) ||
               extensionName.Equals("TypeExtension", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveTypeMarkupPayload(MarkupExtensionInfo markup)
    {
        if (markup.NamedArguments.TryGetValue("TypeName", out var typeNameArgument))
        {
            return typeNameArgument;
        }

        if (markup.NamedArguments.TryGetValue("Type", out var typeArgument))
        {
            return typeArgument;
        }

        if (markup.PositionalArguments.Length == 1)
        {
            return markup.PositionalArguments[0];
        }

        return null;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }
}
