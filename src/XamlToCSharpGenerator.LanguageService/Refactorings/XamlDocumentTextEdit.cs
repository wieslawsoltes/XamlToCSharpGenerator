using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

public sealed record XamlDocumentTextEdit(SourceRange Range, string NewText);
