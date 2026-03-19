namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlParameterInformation(
    string Label,
    string? Documentation = null);
