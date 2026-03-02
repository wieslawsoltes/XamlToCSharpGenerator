namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlHoverInfo(string Markdown, SourceRange? Range = null);
