---
title: "Tooling Stack Packages"
---

# Tooling Stack Packages

This guide explains how the editor/tooling artifacts relate to one another and which one to choose for standalone editor hosting, VS Code usage, or in-app editing.

## Stack layout

| Layer | Packages / artifacts | Purpose |
| --- | --- | --- |
| Shared remote protocol | `XamlToCSharpGenerator.RemoteProtocol` | reusable JSON-RPC framing and shared preview, MCP, and studio contracts |
| Semantic engine | `XamlToCSharpGenerator.LanguageService` | completion, hover, definitions, references, rename, semantic tokens, inlay hints |
| Standalone LSP host | `XamlToCSharpGenerator.LanguageServer.Tool` | packaged `dotnet tool` for editor integrations |
| Standalone MCP host | `XamlToCSharpGenerator.McpServer.Tool` | packaged `dotnet tool` for workspace MCP queries |
| Preview helper host | `XamlToCSharpGenerator.PreviewerHost` | preview helper transport plus preview MCP host for explicit preview lifecycle and hot reload control |
| VS Code integration | `xamltocsharpgenerator.axsg-language-server` | client middleware, server startup, inline C# projections, editor integration |
| In-app editor host | `XamlToCSharpGenerator.Editor.Avalonia` | AvaloniaEdit-based AXAML editor control |

## Recommended entry points

### VS Code user

Use:

- [Artifact: VS Code Extension](vscode-extension/)

This is the supported packaged experience. It bundles the client and uses the managed server plus editor-specific behaviors.

### Custom editor or standalone LSP host

Use:

- [Package: XamlToCSharpGenerator.LanguageServer.Tool](language-server-tool/)

This is the correct entry point when you want a process-based server rather than embedding the semantic engine.

### MCP-based tooling or AI integration

Use:

- [Package: XamlToCSharpGenerator.McpServer.Tool](mcp-server-tool/)

and, when you need to build your own host or adapter:

- [Package: XamlToCSharpGenerator.RemoteProtocol](remote-protocol/)
- [Artifact: XamlToCSharpGenerator.PreviewerHost](preview-host/)

### In-process integration

Use:

- [Package: XamlToCSharpGenerator.LanguageService](language-service/)

and optionally:

- [Package: XamlToCSharpGenerator.Editor.Avalonia](editor-avalonia/)

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

Use the package guide for the standalone host surface:

- [LanguageServer.Tool package guide](language-server-tool/)

The concrete host implementation is intentionally internal.

### `McpServer.Tool`

Owns:

- workspace MCP hosting
- shared MCP capability advertisement
- workspace query tool and resource exposure over the AXSG remote-operation layer

Use the package guide for the standalone host surface:

- [McpServer.Tool package guide](mcp-server-tool/)

### `RemoteProtocol`

Owns:

- JSON-RPC framing shared by LSP and MCP hosts
- preview request, response, event, and hot-reload contracts
- studio remote contracts
- the reusable MCP server core

### `PreviewerHost`

Owns:

- preview session startup and stop orchestration
- preview lifecycle resources in MCP mode
- `axsg.preview.hotReload` and `axsg.preview.update`
- the outer helper bridge to the inner designer host

This is the correct artifact when preview itself is the remote-controlled system.

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

- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/)
- [Tooling Surface](../concepts/tooling-surface/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Artifact: XamlToCSharpGenerator.PreviewerHost](preview-host/)
- [VS Code and Language Service](../guides/vscode-language-service/)
