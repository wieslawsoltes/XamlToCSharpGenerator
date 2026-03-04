using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public readonly record struct AvaloniaSymbolSourceLocation(string Uri, SourceRange Range);
