namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenResourceDescriptor(
    string Uri,
    string Key,
    string TypeName,
    string RawXaml);
