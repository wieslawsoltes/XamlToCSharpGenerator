namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlWorkspaceSymbol(
    string Name,
    XamlDocumentSymbolKind Kind,
    string Uri,
    SourceRange Range,
    string? ContainerName = null);
