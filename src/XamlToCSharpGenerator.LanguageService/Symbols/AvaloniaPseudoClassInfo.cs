namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed record AvaloniaPseudoClassInfo(
    string Name,
    string DeclaringTypeFullName,
    string DeclaringAssemblyName,
    AvaloniaSymbolSourceLocation? SourceLocation = null);
