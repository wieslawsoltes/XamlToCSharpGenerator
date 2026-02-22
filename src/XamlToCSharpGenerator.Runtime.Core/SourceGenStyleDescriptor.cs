namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenStyleDescriptor(
    string Uri,
    string? Key,
    string Selector,
    string? TargetTypeName,
    string RawXaml);
