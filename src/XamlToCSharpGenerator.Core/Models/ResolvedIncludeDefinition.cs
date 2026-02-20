namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedIncludeDefinition(
    string Kind,
    string Source,
    string MergeTarget,
    bool IsAbsoluteUri,
    string? ResolvedSourceUri,
    bool IsProjectLocal,
    string RawXaml,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
