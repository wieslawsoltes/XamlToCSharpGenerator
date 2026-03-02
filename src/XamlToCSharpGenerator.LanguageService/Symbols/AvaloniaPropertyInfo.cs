namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed record AvaloniaPropertyInfo(
    string Name,
    string TypeName,
    bool IsSettable,
    bool IsAttached);
