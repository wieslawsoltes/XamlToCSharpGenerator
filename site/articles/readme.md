---
title: "Documentation"
---

# Documentation

This site covers AXSG from four angles:

- how to install and use it in an application
- how the compiler/runtime/tooling model works
- how to operate, debug, and publish it
- how to navigate the generated API by package, assembly, namespace, and feature

## Read by intent

### I am new to the project

Start with:

1. [Getting Started](getting-started/)
2. [XAML Features](xaml/)
3. [Guides](guides/)

### I need to understand why the compiler behaves a certain way

Start with:

1. [Concepts](concepts/)
2. [Architecture](architecture/)
3. [Reference](reference/)

### I need to ship, maintain, or extend AXSG itself

Start with:

1. [Advanced](advanced/)
2. [Reference](reference/)
3. [Guides](guides/)

## Package-first entry points

- [Package Catalog](reference/package-catalog.md)
- [Package Guides](reference/packages/)
- [Package Selection and Integration](guides/package-selection-and-integration.md)
- [API Coverage Index](reference/api-coverage-index.md)
- [Assembly Catalog](reference/assembly-catalog.md)

## Feature-first entry points

- [Compiled Bindings](xaml/compiled-bindings.md)
- [C# Expressions](xaml/csharp-expressions.md)
- [Inline C#](xaml/inline-csharp.md)
- [Hot Reload and Hot Design](guides/hot-reload-and-hot-design.md)
- [VS Code and Language Service](guides/vscode-language-service.md)
- [Navigation and Refactorings](guides/navigation-and-refactorings.md)

## Operational entry points

- [Troubleshooting](guides/troubleshooting.md)
- [Packaging and Release](guides/packaging-and-release.md)
- [Lunet Docs Pipeline](reference/lunet-docs-pipeline.md)

## Artifact families

- app/build surface: `XamlToCSharpGenerator`, `XamlToCSharpGenerator.Build`
- compiler surface: `Compiler`, `Core`, `Framework.Abstractions`, `Avalonia`, `NoUi`, `Generator`
- runtime surface: `Runtime`, `Runtime.Core`, `Runtime.Avalonia`
- tooling surface: `LanguageService`, `LanguageServer.Tool`, `Editor.Avalonia`, VS Code extension
