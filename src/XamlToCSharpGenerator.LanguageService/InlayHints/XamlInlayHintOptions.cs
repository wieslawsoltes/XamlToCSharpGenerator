namespace XamlToCSharpGenerator.LanguageService.InlayHints;

public enum XamlInlayHintTypeDisplayStyle
{
    Short,
    Qualified
}

public sealed record XamlInlayHintOptions(
    bool EnableBindingTypeHints = true,
    XamlInlayHintTypeDisplayStyle TypeDisplayStyle = XamlInlayHintTypeDisplayStyle.Short)
{
    public static XamlInlayHintOptions Default { get; } = new();
}
