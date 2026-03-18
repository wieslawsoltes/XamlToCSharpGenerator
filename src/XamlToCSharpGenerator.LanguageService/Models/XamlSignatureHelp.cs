using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlSignatureHelp(
    ImmutableArray<XamlSignatureInformation> Signatures,
    int ActiveSignature,
    int ActiveParameter);
