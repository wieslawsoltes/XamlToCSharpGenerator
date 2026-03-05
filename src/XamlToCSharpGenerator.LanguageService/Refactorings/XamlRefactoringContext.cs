using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

public sealed record XamlRefactoringContext(
    string Uri,
    SourcePosition Position,
    XamlLanguageServiceOptions Options,
    string? DocumentTextOverride);
