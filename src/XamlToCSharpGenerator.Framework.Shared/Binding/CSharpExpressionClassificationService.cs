using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class CSharpExpressionClassificationService
{
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly ImmutableHashSet<string> _knownMarkupExtensionNames;
    private readonly TryResolveMarkupExtensionTypeDelegate _tryResolveMarkupExtensionType;

    public delegate bool TryResolveMarkupExtensionTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string markupName,
        out INamedTypeSymbol? extensionType);

    public CSharpExpressionClassificationService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        ImmutableHashSet<string> knownMarkupExtensionNames,
        TryResolveMarkupExtensionTypeDelegate tryResolveMarkupExtensionType)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension;
        _knownMarkupExtensionNames = knownMarkupExtensionNames;
        _tryResolveMarkupExtensionType = tryResolveMarkupExtensionType;
    }

    public bool TryParseCSharpExpressionMarkup(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        bool csharpExpressionsEnabled,
        bool implicitCSharpExpressionsEnabled,
        out string csharpExpressionCode,
        out bool isExplicitExpression)
    {
        csharpExpressionCode = string.Empty;
        isExplicitExpression = false;

        if (csharpExpressionsEnabled &&
            CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                value,
                implicitExpressionsEnabled: false,
                looksLikeMarkupExtensionStart: static _ => false,
                out var rawExpression,
                out var isExplicitMarkupExpression,
                out _) &&
            isExplicitMarkupExpression)
        {
            csharpExpressionCode = rawExpression;
            isExplicitExpression = true;
            return true;
        }

        if (!implicitCSharpExpressionsEnabled)
        {
            return false;
        }

        if (!CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                value,
                implicitExpressionsEnabled: true,
                innerExpression => LooksLikeMarkupExtensionStart(innerExpression, compilation, document),
                out var implicitExpression,
                out var isImplicitlyExplicitExpression,
                out _)
            || isImplicitlyExplicitExpression)
        {
            return false;
        }

        csharpExpressionCode = implicitExpression;
        isExplicitExpression = false;
        return true;
    }

    private bool LooksLikeMarkupExtensionStart(
        string innerExpression,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (string.IsNullOrWhiteSpace(innerExpression))
        {
            return false;
        }

        var candidateMarkup = "{" + innerExpression.Trim() + "}";
        if (!_tryParseMarkupExtension(candidateMarkup, out var markup))
        {
            return false;
        }

        if (_knownMarkupExtensionNames.Contains(markup.Name))
        {
            return true;
        }

        return _tryResolveMarkupExtensionType(compilation, document, markup.Name, out _);
    }
}
