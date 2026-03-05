namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlInlayHint(
    SourcePosition Position,
    string Label,
    XamlInlayHintKind Kind,
    string? Tooltip = null,
    bool PaddingLeft = true,
    bool PaddingRight = false);
