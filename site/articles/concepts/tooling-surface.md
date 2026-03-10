---
title: "Tooling Surface"
---

# Tooling Surface

AXSG ships multiple tooling layers because editor integrations, in-process editors, and build-time compiler analysis have different hosting constraints.

## Tooling layers

### Language service core

`XamlToCSharpGenerator.LanguageService` is the shared semantic engine. It owns:

- analysis over XAML + project compilations
- completion
- hover
- definitions, declarations, and references
- rename/refactoring propagation
- semantic tokens and inlay hints
- inline C# projection support

### Standalone language server

`XamlToCSharpGenerator.LanguageServer.Tool` hosts the language service over LSP and is the server surface used by the VS Code extension and other editor integrations.

### VS Code extension

The VSIX bundles:

- the client middleware
- activation strategy and virtual-document interop
- startup and projection management
- fallback logic around AXSG and editor-native providers

### Embedded editor control

`XamlToCSharpGenerator.Editor.Avalonia` packages an in-process editor surface for Avalonia applications using AvaloniaEdit and the AXSG language service.

## Why these are separate

- build-time semantics need Roslyn/MSBuild access
- LSP hosting needs process boundaries and transport concerns
- embedded editors need in-process APIs rather than LSP
- VS Code needs client-side middleware and activation rules that do not belong in the server

## Choose the right surface

- use the VSIX if you are a VS Code user
- use the .NET tool if you are integrating another editor
- use `LanguageService` directly if you are embedding editor features in-process
- use `Editor.Avalonia` if you need a product-quality AXAML editor inside an Avalonia app

## Related docs

- [Language Service and VS Code](../architecture/language-service-and-vscode)
- [VS Code Language Service](../guides/vscode-language-service)
- [Package: XamlToCSharpGenerator.LanguageService](../reference/packages/language-service)
- [Package: XamlToCSharpGenerator.LanguageServer.Tool](../reference/packages/language-server-tool)
- [Package: XamlToCSharpGenerator.Editor.Avalonia](../reference/packages/editor-avalonia)
