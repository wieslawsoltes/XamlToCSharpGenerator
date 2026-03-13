---
title: "Package Selection and Integration"
---

# Package Selection and Integration

Use this guide when you need to decide between the umbrella package, lower-level compiler packages, runtime-only packages, or the standalone tooling artifacts.

## Default app setup

Most Avalonia applications should start with:

- `XamlToCSharpGenerator`

That gives you:

- build imports
- source generator payload
- runtime support packages

Use the umbrella package unless you have a concrete reason to separate compiler, runtime, or tooling layers.

For the normal Avalonia app path, the minimum supported setup is:

- package reference to `XamlToCSharpGenerator`
- `<AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>`
- `.UseAvaloniaSourceGeneratedXaml()` on `AppBuilder`

If you need a custom project item group, use `XamlSourceGenInputItemGroup` and mirror your XAML items into that group. Do not override `XamlSourceGenAdditionalFilesSourceItemGroup` for Avalonia consumers; the build package always projects AXSG Avalonia inputs into Roslyn `AdditionalFiles` as `AvaloniaXaml`.

## When to split packages

### Build-only integration

Pick `XamlToCSharpGenerator.Build` when:

- you need explicit control over imported targets
- you are composing a custom SDK/build story
- you want AXSG build behavior without the full umbrella package

### Compiler/profile integration

Pick the lower-level compiler packages when you are extending AXSG itself or embedding it:

- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Framework.Abstractions`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.NoUi`

### Runtime-only integration

Pick runtime packages directly when you are hosting generated output or hot reload/runtime services:

- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`

### Tooling/editor integration

Pick tooling packages when you need editor/language infrastructure:

- `XamlToCSharpGenerator.LanguageService`
- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.Editor.Avalonia`
- `xamltocsharpgenerator.axsg-language-server`

## Recommended combinations

| Scenario | Recommended artifacts |
| --- | --- |
| Standard Avalonia app | `XamlToCSharpGenerator` |
| Custom compiler host | `Compiler`, `Core`, `Framework.Abstractions`, plus a profile package |
| Runtime host integration | `Runtime.Core` and `Runtime.Avalonia` |
| External editor integration | `LanguageServer.Tool` |
| In-app AXAML editor | `Editor.Avalonia` plus `LanguageService` |
| VS Code authoring | VS Code extension, optionally alongside the standalone tool |

## Related reference pages

- [Package Catalog](../reference/package-catalog/)
- [Package and Assembly](../reference/package-and-assembly/)
- [Assembly Catalog](../reference/assembly-catalog/)
- [Package Guides](../reference/package-guides/)
