namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlDocumentLink(
    SourceRange Range,
    string TargetUri,
    string? Tooltip = null);
