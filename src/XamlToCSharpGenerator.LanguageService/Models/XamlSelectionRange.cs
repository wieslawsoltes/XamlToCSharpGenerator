namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlSelectionRange(SourceRange Range, XamlSelectionRange? Parent);
