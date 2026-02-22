namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenIncludeEdgeDescriptor(
    string SourceUri,
    string IncludedUri,
    string MergeTarget,
    int Order);
