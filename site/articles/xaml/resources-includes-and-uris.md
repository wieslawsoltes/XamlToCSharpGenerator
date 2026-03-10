---
title: "Resources, Includes, and URIs"
---

# Resources, Includes, and URIs

AXSG tracks XAML resources, include graphs, and URI-valued properties as part of compilation and language-service analysis.

## Resource keys

Resource keys participate in:

- compile-time lookup for supported source-generated paths
- definition/reference navigation from `StaticResource` and `DynamicResource`
- rename/refactoring across declarations and usages

Example:

```xaml
<SolidColorBrush x:Key="AccentBrush" Color="#0A84FF" />
<TextBlock Foreground="{StaticResource AccentBrush}" />
```

## Include graphs

AXSG understands:

- relative include paths
- rooted project paths such as `/Themes/Fluent.xaml`
- `avares://...` URIs
- linked XAML items surfaced through `Link` or `TargetPath`

Example:

```xaml
<StyleInclude Source="/Themes/Fluent.xaml" />
```

Definition/declaration navigation on the URI resolves to the included XAML file, not just the raw string value.

## Why include graphs matter

Include graph analysis is used for more than file opening. It also feeds:

- generated URI registration
- cycle and duplicate detection
- resource and theme resolution
- hot reload targeting for included/linked XAML

## Control themes and resource dictionaries

AXSG uses include/resource knowledge to validate:

- duplicate generated URIs
- missing included documents
- include cycles
- local control-theme `BasedOn` chains versus normal external override patterns

## Tooling support

The language service resolves:

- resource key definitions and references
- URI definitions for include sources
- linked-XAML references across the project graph
- resource-key rename/refactoring across declarations and usages

## Related docs

- [Styles, Templates, and Themes](styles-templates-and-themes.md)
- [Compiled Bindings](compiled-bindings.md)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback.md)
- [Troubleshooting](../guides/troubleshooting.md)
