namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlTemplateDefinition(
    string Kind,
    string? Key,
    string? TargetType,
    string? DataType,
    string RawXaml,
    int Line,
    int Column);
