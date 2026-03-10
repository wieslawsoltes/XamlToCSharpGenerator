---
title: "Language Service and Tooling Namespaces"
---

# Language Service and Tooling Namespaces

This namespace family covers editor semantics, LSP request handling, projected inline-code support, and the tooling surface shared by the VS Code extension and in-app editors.

## Packages behind this area

- `XamlToCSharpGenerator.LanguageService`
- `XamlToCSharpGenerator.LanguageServer.Tool`
- VS Code extension
- `XamlToCSharpGenerator.Editor.Avalonia`

## Primary namespaces

- <xref:XamlToCSharpGenerator.LanguageService>
- <xref:XamlToCSharpGenerator.LanguageService.Analysis>
- <xref:XamlToCSharpGenerator.LanguageService.Completion>
- <xref:XamlToCSharpGenerator.LanguageService.Definitions>
- <xref:XamlToCSharpGenerator.LanguageService.Documents>
- <xref:XamlToCSharpGenerator.LanguageService.Hover>
- <xref:XamlToCSharpGenerator.LanguageService.InlayHints>
- <xref:XamlToCSharpGenerator.LanguageService.InlineCode>
- <xref:XamlToCSharpGenerator.LanguageService.Refactorings>
- <xref:XamlToCSharpGenerator.LanguageService.SemanticTokens>
- <xref:XamlToCSharpGenerator.LanguageService.Symbols>
- <xref:XamlToCSharpGenerator.LanguageService.Workspace>
- <xref:XamlToCSharpGenerator.Editor.Avalonia>
- [Language server generated API](/api/XamlToCSharpGenerator.LanguageServer/)

## What lives here

### Shared semantic engine

The `LanguageService` namespaces own completion, hover, navigation, references, rename, semantic highlighting, inlay hints, and cross-file reference analysis.

### Inline-code tooling

`LanguageService.InlineCode` and related navigation/completion services analyze inline C# snippets and can project them into temporary C# documents when editor-side interoperability is useful.

### Server transport and protocol

`LanguageServer` contains the standalone LSP host, request routing, metadata virtual-document support, and the transport/framing layer.

### In-process editor hosting

`Editor.Avalonia` wraps the semantic engine for AvaloniaEdit-based in-app editor scenarios.

## Use this area when

- you are debugging completion, definitions, references, or rename behavior
- you are extending VS Code integration or another editor host
- you are profiling startup, workspace load, or cross-file reference performance
- you are working on inline-C# editing support

## Suggested API entry points

- <xref:XamlToCSharpGenerator.LanguageService.XamlLanguageServiceEngine>
- <xref:XamlToCSharpGenerator.LanguageService.Definitions.XamlReferenceService>
- <xref:XamlToCSharpGenerator.LanguageService.Completion.XamlCompletionService>
- <xref:XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor>
- [Language server generated API root](/api/XamlToCSharpGenerator.LanguageServer/)

The concrete server host `AxsgLanguageServer` is intentionally internal. Treat the package guide and generated namespace root as the supported documentation surface for that host.

## Related docs

- [Tooling Surface](../concepts/tooling-surface.md)
- [Language Service and VS Code](../architecture/language-service-and-vscode.md)
- [VS Code and Language Service](../guides/vscode-language-service.md)
- [Navigation and Refactorings](../guides/navigation-and-refactorings.md)
