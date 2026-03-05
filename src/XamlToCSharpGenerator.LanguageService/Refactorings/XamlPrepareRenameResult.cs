using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

public sealed record XamlPrepareRenameResult(SourceRange Range, string Placeholder);
