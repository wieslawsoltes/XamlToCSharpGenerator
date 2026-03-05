using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

public sealed record XamlRefactoringCommand(
    string Name,
    string Uri,
    SourcePosition Position,
    string? RefactoringId = null);
