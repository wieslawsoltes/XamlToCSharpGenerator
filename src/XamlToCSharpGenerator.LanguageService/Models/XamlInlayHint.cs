using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlInlayHint(
    SourcePosition Position,
    string Label,
    XamlInlayHintKind Kind,
    string? Tooltip = null,
    bool PaddingLeft = true,
    bool PaddingRight = false,
    ImmutableArray<XamlInlayHintLabelPart> LabelParts = default);

public sealed record XamlInlayHintLabelPart(
    string Value,
    string? Tooltip = null,
    AvaloniaSymbolSourceLocation? DefinitionLocation = null);
