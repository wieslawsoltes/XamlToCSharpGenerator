namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenBindingSourceKind
{
    DataContext = 0,
    Root = 1,
    Target = 2,
    ElementName = 3,
    TemplatedParent = 4,
    FindAncestor = 5,
    ExplicitSource = 6
}

public readonly record struct SourceGenBindingDependency(
    SourceGenBindingSourceKind SourceKind,
    string? Path,
    string? ElementName,
    global::Avalonia.Data.RelativeSource? RelativeSource = null,
    object? Source = null);
