using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedStyleDefinition(
    string? Key,
    string Selector,
    string? TargetTypeName,
    ImmutableArray<ResolvedSetterDefinition> Setters,
    string RawXaml,
    int Line,
    int Column);
