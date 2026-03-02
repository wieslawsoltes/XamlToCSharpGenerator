namespace XamlToCSharpGenerator.LanguageService.Completion;

public sealed record XamlCompletionContext(
    XamlCompletionContextKind Kind,
    string Token,
    string? CurrentElementName,
    string? CurrentAttributeName,
    string? CurrentAttributeValue,
    int TokenStartOffset,
    int TokenEndOffset);
