namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlIncludeDefinition(
    string Kind,
    string Source,
    string MergeTarget,
    string RawXaml,
    int Line,
    int Column);
