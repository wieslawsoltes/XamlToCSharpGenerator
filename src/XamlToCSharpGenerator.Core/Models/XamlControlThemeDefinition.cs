using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlControlThemeDefinition(
    string? Key,
    string? TargetType,
    string? BasedOn,
    string? ThemeVariant,
    string? DataType,
    bool? CompileBindings,
    ImmutableArray<XamlSetterDefinition> Setters,
    string RawXaml,
    int Line,
    int Column);
