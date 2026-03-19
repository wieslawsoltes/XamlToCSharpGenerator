namespace XamlToCSharpGenerator.LanguageService.Formatting;

public readonly record struct XamlFormattingOptions(int TabSize, bool InsertSpaces)
{
    public static XamlFormattingOptions Default { get; } = new(4, true);

    public XamlFormattingOptions Normalize()
    {
        var tabSize = TabSize <= 0 ? Default.TabSize : TabSize;
        return new XamlFormattingOptions(tabSize, InsertSpaces);
    }
}
