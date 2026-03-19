using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlLinkedEditingRanges(
    ImmutableArray<SourceRange> Ranges,
    string? WordPattern);
