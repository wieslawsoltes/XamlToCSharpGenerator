namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedTemplateDefinition(
    string Kind,
    string? Key,
    string? TargetTypeName,
    string? DataType,
    string RawXaml,
    int Line,
    int Column);
