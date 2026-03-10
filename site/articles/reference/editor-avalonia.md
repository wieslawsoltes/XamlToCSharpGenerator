---
title: "Package: XamlToCSharpGenerator.Editor.Avalonia"
---

# XamlToCSharpGenerator.Editor.Avalonia

## Role

An AvaloniaEdit-based editor control layered over the AXSG language service for in-app AXAML authoring.

## Related namespaces

- <xref:XamlToCSharpGenerator.Editor.Avalonia>

## Use it when

- you want to embed an AXAML editor inside an Avalonia application
- you need AXSG completion, semantic tokens, or navigation in-process
- you want editor behavior backed by the same language-service engine used by the standalone server

## What it adds

This package layers:

- an AvaloniaEdit-based editor host
- AXSG language-service integration
- document/update plumbing suitable for in-app editing surfaces
- editor-facing abstractions over syntax highlighting, diagnostics, and semantic updates

It is intended for product-quality editor surfaces, not just sample or test usage.

## Typical use cases

Use this package when you are building:

- an in-app AXAML editor
- a design tool that needs editing plus semantic services
- a desktop workflow where the language service must run in-process

## Dependency shape

This package sits on top of:

- `XamlToCSharpGenerator.LanguageService`
- `Avalonia`
- `Avalonia.AvaloniaEdit`

That means it is best suited for in-process editor experiences inside AXSG-aware desktop tools or product shells.

## What it does not replace

If you only need the semantic engine, use `XamlToCSharpGenerator.LanguageService` directly. If you need an out-of-process LSP server, use `XamlToCSharpGenerator.LanguageServer.Tool`.

## Related docs

- [Tooling Surface](../concepts/tooling-surface/)
- [Language Service and VS Code](../../architecture/language-service-and-vscode/)
- [VS Code and Language Service](../guides/vscode-language-service/)
- [Runtime and Editor Namespaces](../namespace-runtime-and-editor/)
