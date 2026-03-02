namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlSemanticToken(int Line, int Character, int Length, string TokenType);
