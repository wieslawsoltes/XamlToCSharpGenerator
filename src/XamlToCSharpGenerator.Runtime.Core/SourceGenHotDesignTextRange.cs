namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignTextRange(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    int StartOffset,
    int EndOffset);
