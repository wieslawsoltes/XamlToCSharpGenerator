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

        if (!CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                value,
                implicitExpressionsEnabled,
                innerExpression => LooksLikeMarkupExtensionStart(innerExpression, compilation, document),
                out var rawExpression,
                out isExplicitExpression,
                out _))
        {
            return false;
        }

        csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawExpression);
        return csharpExpression.Length > 0;
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
