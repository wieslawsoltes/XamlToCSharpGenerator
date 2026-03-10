---
title: "Samples and Feature Tour"
---

# Samples and Feature Tour

AXSG ships sample applications that exercise the compiler, runtime, hot reload paths, and editor tooling. Use the samples first when you want a working baseline instead of assembling features from scratch.

## Main samples

| Sample | Path | Use it for |
| --- | --- | --- |
| Source-generated catalog | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample` | language features, bindings, inline C#, templates, styles, runtime loader, and hot reload |
| Control catalog | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog` | large-scale app integration, theme overrides, selector-heavy styling, mobile hot reload |
| NoUi framework pilot | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/NoUiFrameworkPilotSample` | framework-host reuse outside Avalonia |
| CRUD sample | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample` | app-facing install path and generated runtime behavior |

## Feature tour

Start in `SourceGenXamlCatalogSample` when you need to see a feature in isolation:

- bindings and compiled bindings
- C# expressions and shorthand expressions
- inline C# and `<![CDATA[ ... ]]>`
- event bindings and inline lambdas
- resource includes, `StyleInclude`, `ControlTheme`, and `TemplateBinding`
- global XML namespace mappings
- runtime loader fallback behavior
- language-service navigation and inline editor features

## Suggested reading flow

1. [Quickstart](quickstart.md)
2. [C# Expressions](../guides/csharp-expressions.md)
3. [Inline C# Code](../guides/inline-csharp-code.md)
4. [Styles, Templates, and Themes](../xaml/styles-templates-and-themes.md)
5. [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback.md)
6. [VS Code and Language Service](../guides/vscode-language-service.md)

## Why sample-first matters here

AXSG has several surfaces that interact:

- compiler host configuration
- generated runtime helpers
- hot reload transport/runtime coordination
- editor projections and cross-language navigation

The samples give you a known-good integration point for those layers before you start stripping the setup down for your own project.
