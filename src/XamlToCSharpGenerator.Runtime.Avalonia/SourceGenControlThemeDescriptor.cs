using System;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenControlThemeDescriptor(
    string Uri,
    string? Key,
    string? TargetTypeName,
    string? BasedOn,
    string? ThemeVariant,
    string RawXaml,
    string? BasedOnKey = null,
    string? NormalizedThemeVariant = null,
    Func<ControlTheme>? Factory = null);
