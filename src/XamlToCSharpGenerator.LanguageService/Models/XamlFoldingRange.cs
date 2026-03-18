namespace XamlToCSharpGenerator.LanguageService.Models;

public readonly record struct XamlFoldingRange(int StartLine, int EndLine, string? Kind);
