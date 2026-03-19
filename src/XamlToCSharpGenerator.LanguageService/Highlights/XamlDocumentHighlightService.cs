using System;
using System.Collections.Immutable;
using System.Linq;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Highlights;

internal sealed class XamlDocumentHighlightService
{
    public ImmutableArray<XamlDocumentHighlight> GetDocumentHighlights(
        string uri,
        ImmutableArray<XamlReferenceLocation> references)
    {
        if (references.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlDocumentHighlight>.Empty;
        }

        return references
            .Where(reference => string.Equals(reference.Uri, uri, StringComparison.Ordinal))
            .Select(static reference => new XamlDocumentHighlight(
                reference.Range,
                reference.IsDeclaration ? XamlDocumentHighlightKind.Write : XamlDocumentHighlightKind.Read))
            .Distinct()
            .ToImmutableArray();
    }
}
