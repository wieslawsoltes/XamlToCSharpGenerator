namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public readonly struct SelectorBranchSegment
{
    public SelectorBranchSegment(string text, SelectorCombinatorKind combinator)
        : this(text, combinator, 0, text?.Length ?? 0)
    {
    }

    public SelectorBranchSegment(string text, SelectorCombinatorKind combinator, int start, int length)
    {
        Text = text;
        Combinator = combinator;
        Start = start;
        Length = length;
    }

    public string Text { get; }

    public SelectorCombinatorKind Combinator { get; }

    public int Start { get; }

    public int Length { get; }
}
