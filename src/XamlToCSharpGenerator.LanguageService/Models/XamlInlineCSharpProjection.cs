namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlInlineCSharpProjection(
    string Id,
    string Kind,
    SourceRange XamlRange,
    SourceRange ProjectedCodeRange,
    string ProjectedText);
