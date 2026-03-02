using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlDocumentSymbol(
    string Name,
    XamlDocumentSymbolKind Kind,
    SourceRange Range,
    SourceRange SelectionRange,
    ImmutableArray<XamlDocumentSymbol> Children);
