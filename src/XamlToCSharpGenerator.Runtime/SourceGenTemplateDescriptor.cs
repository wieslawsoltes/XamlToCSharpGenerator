namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenTemplateDescriptor(
    string Uri,
    string Kind,
    string? Key,
    string? TargetTypeName,
    string? DataType,
    string RawXaml);
