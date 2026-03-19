using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlSignatureInformation(
    string Label,
    string? Documentation,
    ImmutableArray<XamlParameterInformation> Parameters);
