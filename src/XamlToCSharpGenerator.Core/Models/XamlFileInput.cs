namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlFileInput(
    string FilePath,
    string TargetPath,
    string SourceItemGroup,
    string Text);
