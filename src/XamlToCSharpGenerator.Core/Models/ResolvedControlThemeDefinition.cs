using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedControlThemeDefinition(
    string? Key,
    string? TargetTypeName,
    string? BasedOn,
    string? ThemeVariant,
    ImmutableArray<ResolvedSetterDefinition> Setters,
    string RawXaml,
    int Line,
    int Column);
