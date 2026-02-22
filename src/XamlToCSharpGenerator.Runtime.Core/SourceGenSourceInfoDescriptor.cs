namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenSourceInfoDescriptor(
    string Uri,
    string Kind,
    string Name,
    string FilePath,
    int Line,
    int Column);
