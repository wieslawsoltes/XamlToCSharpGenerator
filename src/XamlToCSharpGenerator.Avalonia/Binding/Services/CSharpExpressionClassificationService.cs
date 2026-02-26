using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class CSharpExpressionClassificationService
{
    internal delegate bool TryResolveMarkupExtensionTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        out INamedTypeSymbol? typeSymbol);

    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly ImmutableHashSet<string> _knownMarkupExtensionNames;
    private readonly TryResolveMarkupExtensionTypeDelegate _tryResolveMarkupExtensionType;

    public CSharpExpressionClassificationService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        ImmutableHashSet<string> knownMarkupExtensionNames,
        TryResolveMarkupExtensionTypeDelegate tryResolveMarkupExtensionType)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
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

        if (!MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var innerExpression))
        {
            return false;
        }

        if (innerExpression.Length == 0)
        {
            return false;
        }

        if (innerExpression.StartsWith("=", StringComparison.Ordinal))
        {
            var explicitExpression = innerExpression.Substring(1).Trim();
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

        if (!IsImplicitCSharpExpressionMarkup(innerExpression, compilation, document))
        {
            return false;
        }

        csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(innerExpression);
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
        if (!_tryParseMarkupExtension(wrappedExpression, out var markup))
        {
            return false;
        }

        var token = markup.Name;
        if (token.Length == 0)
        {
            return false;
        }

        if (token.IndexOf(':') >= 0)
        {
            return true;
        }

        if (IsKnownMarkupExtensionName(token))
        {
            return true;
        }

        return _tryResolveMarkupExtensionType(compilation, document, token, out _);
    }

    private bool IsKnownMarkupExtensionName(string token)
    {
        foreach (var knownMarkupExtensionName in _knownMarkupExtensionNames)
        {
            if (XamlMarkupExtensionNameSemantics.Matches(token, knownMarkupExtensionName))
            {
                return true;
            }
        }

        return false;
    }
}
