---
title: "Tooling Stack Packages"
---

# Tooling Stack Packages

This guide explains how the editor/tooling artifacts relate to one another and which one to choose for standalone editor hosting, VS Code usage, or in-app editing.

## Stack layout

| Layer | Packages / artifacts | Purpose |
| --- | --- | --- |
| Semantic engine | `XamlToCSharpGenerator.LanguageService` | completion, hover, definitions, references, rename, semantic tokens, inlay hints |
| Standalone LSP host | `XamlToCSharpGenerator.LanguageServer.Tool` | packaged `dotnet tool` for editor integrations |
| VS Code integration | `xamltocsharpgenerator.axsg-language-server` | client middleware, server startup, inline C# projections, editor integration |
| In-app editor host | `XamlToCSharpGenerator.Editor.Avalonia` | AvaloniaEdit-based AXAML editor control |

## Recommended entry points

### VS Code user

Use:

- [Artifact: VS Code Extension](packages/vscode-extension)

This is the supported packaged experience. It bundles the client and uses the managed server plus editor-specific behaviors.

### Custom editor or standalone LSP host

Use:

- [Package: XamlToCSharpGenerator.LanguageServer.Tool](packages/language-server-tool)

This is the correct entry point when you want a process-based server rather than embedding the semantic engine.

### In-process integration

Use:

- [Package: XamlToCSharpGenerator.LanguageService](packages/language-service)

and optionally:

- [Package: XamlToCSharpGenerator.Editor.Avalonia](packages/editor-avalonia)

if the host is an Avalonia app and you want an editor surface as well.

## Responsibilities by layer

### `LanguageService`

Owns:

- XAML analysis
- completion, hover, definitions, declarations, references
- rename and cross-language propagation
- semantic tokens and inlay hints
- inline C# projection analysis

Primary API entry points:

- <xref:XamlToCSharpGenerator.LanguageService.XamlLanguageServiceEngine>
- <xref:XamlToCSharpGenerator.LanguageService.Definitions.XamlReferenceService>
- <xref:XamlToCSharpGenerator.LanguageService.Completion.XamlCompletionService>

### `LanguageServer.Tool`

Owns:

- LSP transport
- request routing
- metadata document and projection endpoints
- standalone server packaging via `dotnet tool`

Use the generated namespace root for public API browsing:

- [Language server generated API](/api/XamlToCSharpGenerator.LanguageServer/)

The concrete host implementation is intentionally internal.

### VS Code extension

Owns:

- activation strategy
- lazy server startup
- editor middleware
- inline C# projected document interop
- VS Code-specific command wiring

This is documented narratively, not as generated API.

### `Editor.Avalonia`

Owns:

- the AvaloniaEdit-based in-app editor surface
- document/workspace wiring for in-process editing
- semantic-service integration in Avalonia applications

Primary API entry point:

- <xref:XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor>

## Related docs

- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling)
- [Tooling Surface](../concepts/tooling-surface)
- [Language Service and VS Code](../architecture/language-service-and-vscode)
- [VS Code and Language Service](../guides/vscode-language-service)
