---
title: "Language Service and VS Code"
---

# Language Service and VS Code

AXSG’s editor stack is layered so the same semantic engine can serve the standalone LSP host, the VS Code extension, and in-process editors.

## Stack overview

### Language service core

`XamlToCSharpGenerator.LanguageService` owns semantic understanding of:

- XAML documents and scopes
- bindings, selectors, resources, and property elements
- inline C# and CDATA projections
- navigation, references, rename, hover, completion, semantic tokens, and inlay hints

### Language server host

`XamlToCSharpGenerator.LanguageServer.Tool` exposes that engine over LSP and handles:

- request routing
- protocol payload shaping
- projection/document endpoints
- metadata/source-link virtual documents

### VS Code extension

The VS Code extension adds client-side concerns:

- activation rules
- projected virtual C# documents for inline code interop
- AXSG-first vs editor-native provider fallback behavior
- status bar and editor wiring

## Cross-language design

The language-service stack supports:

- XAML to C# navigation
- C# to XAML references and rename propagation
- inline C# editing inside XAML
- projected C# interop where existing editor providers help without taking ownership of XAML semantics

## Performance considerations

The editor stack has explicit performance work in:

- deferred `MSBuildWorkspace` creation
- cached project/source snapshots
- low-allocation reference discovery
- AXSG-first request handling before projected C# fallback

## Related docs

- [Tooling Surface](../concepts/tooling-surface)
- [VS Code Language Service](../guides/vscode-language-service)
- [Navigation and Refactorings](../guides/navigation-and-refactorings)
- [Package: XamlToCSharpGenerator.LanguageService](../reference/packages/language-service)
- [Package: VS Code Extension](../reference/packages/vscode-extension)
