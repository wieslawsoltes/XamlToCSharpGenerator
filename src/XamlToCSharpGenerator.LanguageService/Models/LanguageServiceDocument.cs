namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record LanguageServiceDocument(
    string Uri,
    string FilePath,
    string Text,
    int Version);
