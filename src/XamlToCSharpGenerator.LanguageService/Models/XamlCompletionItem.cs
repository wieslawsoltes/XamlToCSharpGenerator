namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlCompletionItem(
    string Label,
    string InsertText,
    XamlCompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null,
    bool IsDeprecated = false,
    SourceRange? ReplaceRange = null);
