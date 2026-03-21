namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenBindingSourceKind
{
    DataContext = 0,
    Root = 1,
    Target = 2,
    ElementName = 3
}

public readonly record struct SourceGenBindingDependency(
    SourceGenBindingSourceKind SourceKind,
    string? Path,
    string? ElementName);
