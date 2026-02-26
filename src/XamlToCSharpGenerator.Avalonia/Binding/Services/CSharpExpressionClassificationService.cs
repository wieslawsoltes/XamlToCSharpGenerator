using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class CSharpExpressionClassificationService
{
    internal delegate bool TryResolveMarkupExtensionTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        out INamedTypeSymbol? typeSymbol);

    private readonly MarkupExpressionParser _markupExpressionParser;
    private readonly ImmutableHashSet<string> _knownMarkupExtensionNames;
    private readonly TryResolveMarkupExtensionTypeDelegate _tryResolveMarkupExtensionType;

    public CSharpExpressionClassificationService(
        MarkupExpressionParser markupExpressionParser,
        ImmutableHashSet<string> knownMarkupExtensionNames,
        TryResolveMarkupExtensionTypeDelegate tryResolveMarkupExtensionType)
    {
        _markupExpressionParser = markupExpressionParser ?? throw new ArgumentNullException(nameof(markupExpressionParser));
        _knownMarkupExtensionNames = knownMarkupExtensionNames ?? throw new ArgumentNullException(nameof(knownMarkupExtensionNames));
        _tryResolveMarkupExtensionType = tryResolveMarkupExtensionType ??
                                         throw new ArgumentNullException(nameof(tryResolveMarkupExtensionType));
    }

    public bool TryParseCSharpExpressionMarkup(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        bool expressionsEnabled,
        bool implicitExpressionsEnabled,
        out string csharpExpression,
        out bool isExplicitExpression)
    {
        csharpExpression = string.Empty;
        isExplicitExpression = false;

        if (!expressionsEnabled ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Length > 3 &&
            trimmed[1] == '=')
        {
            var explicitExpression = trimmed.Substring(2, trimmed.Length - 3).Trim();
            if (explicitExpression.Length == 0)
            {
                return false;
            }

            csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(explicitExpression);
            isExplicitExpression = true;
            return csharpExpression.Length > 0;
        }

        if (!implicitExpressionsEnabled)
        {
            return false;
        }

        var implicitExpression = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (!IsImplicitCSharpExpressionMarkup(implicitExpression, compilation, document))
        {
            return false;
        }

        csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(implicitExpression);
        return csharpExpression.Length > 0;
    }

    private bool IsImplicitCSharpExpressionMarkup(
        string expressionBody,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (string.IsNullOrWhiteSpace(expressionBody))
        {
            return false;
        }

        if (LooksLikeMarkupExtensionStart(expressionBody, compilation, document))
        {
            return false;
        }

        var trimmed = expressionBody.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("(", StringComparison.Ordinal) ||
            trimmed.StartsWith("!", StringComparison.Ordinal) ||
            trimmed.StartsWith("new ", StringComparison.Ordinal) ||
            trimmed.StartsWith("$\"", StringComparison.Ordinal) ||
            trimmed.StartsWith("$'", StringComparison.Ordinal) ||
            trimmed.StartsWith("typeof(", StringComparison.Ordinal) ||
            trimmed.StartsWith("nameof(", StringComparison.Ordinal) ||
            trimmed.StartsWith("default(", StringComparison.Ordinal) ||
            trimmed.StartsWith("sizeof(", StringComparison.Ordinal) ||
            trimmed.StartsWith(".", StringComparison.Ordinal) ||
            trimmed.StartsWith("this.", StringComparison.Ordinal) ||
            trimmed.StartsWith("BindingContext.", StringComparison.Ordinal))
        {
            return true;
        }

        if (CSharpExpressionTextSemantics.ContainsImplicitExpressionOperator(trimmed))
        {
            return true;
        }

        if (CSharpExpressionTextSemantics.IsMethodCallLikeExpression(trimmed) ||
            CSharpExpressionTextSemantics.IsMemberAccessLikeExpression(trimmed))
        {
            return true;
        }

        return CSharpExpressionTextSemantics.IsBareIdentifierExpression(trimmed);
    }

    private bool LooksLikeMarkupExtensionStart(
        string expressionBody,
        Compilation compilation,
        XamlDocumentModel document)
    {
        var wrappedExpression = "{" + expressionBody + "}";
        if (!_markupExpressionParser.TryParseMarkupExtension(wrappedExpression, out var markup))
        {
            return false;
        }

        var token = markup.Name;
        if (token.Length == 0)
        {
            return false;
        }

        if (token.Contains(':', StringComparison.Ordinal))
        {
            return true;
        }

        if (_knownMarkupExtensionNames.Contains(token) ||
            _knownMarkupExtensionNames.Contains(token + "Extension"))
        {
            return true;
        }

        return _tryResolveMarkupExtensionType(compilation, document, token, out _);
    }
}
