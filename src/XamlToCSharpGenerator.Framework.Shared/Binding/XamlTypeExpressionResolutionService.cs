using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XamlTypeExpressionResolutionService
{
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> _resolveTypeToken;

    public XamlTypeExpressionResolutionService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> resolveTypeToken)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension;
        _resolveTypeToken = resolveTypeToken;
    }

    public INamedTypeSymbol? ResolveTypeFromExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace)
    {
        _ = fallbackClrNamespace;
        if (string.IsNullOrWhiteSpace(typeExpression))
        {
            return null;
        }

        if (_tryParseMarkupExtension(typeExpression!, out var markup) &&
            XamlMarkupExtensionNameSemantics.Classify(markup.Name) == XamlMarkupExtensionKind.Type)
        {
            var typeToken = markup.NamedArguments.TryGetValue("TypeName", out var namedTypeName)
                ? namedTypeName
                : (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
            if (!string.IsNullOrWhiteSpace(typeToken))
            {
                return _resolveTypeToken(compilation, document, typeToken);
            }
        }

        return _resolveTypeToken(compilation, document, typeExpression!);
    }
}
