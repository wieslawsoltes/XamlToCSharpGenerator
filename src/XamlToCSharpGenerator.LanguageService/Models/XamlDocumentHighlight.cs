namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlDocumentHighlight(SourceRange Range, XamlDocumentHighlightKind Kind);
