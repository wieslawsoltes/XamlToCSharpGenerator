namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenIncludeDescriptor(
    string Uri,
    string Kind,
    string Source,
    string MergeTarget,
    bool IsAbsoluteUri,
    string RawXaml);
