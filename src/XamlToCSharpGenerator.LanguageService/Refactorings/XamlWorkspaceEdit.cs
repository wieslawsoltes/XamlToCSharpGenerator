using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

public sealed record XamlWorkspaceEdit(
    ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>> Changes)
{
    public static XamlWorkspaceEdit Empty { get; } = new(
        ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty);

    public bool HasChanges => Changes.Count > 0;
}
