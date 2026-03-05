namespace XamlToCSharpGenerator.LanguageService.Refactorings;

public sealed record XamlRefactoringAction(
    string Title,
    string Kind,
    bool IsPreferred,
    XamlRefactoringCommand Command);
