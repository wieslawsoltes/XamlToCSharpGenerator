---
title: "Unified Remote API and MCP"
---

# Unified Remote API and MCP

AXSG now has one shared remote-operation layer that serves LSP, MCP, preview orchestration, and studio/runtime remote surfaces.

The important design choice is:

- reuse JSON-RPC transport infrastructure across LSP and MCP
- do not reuse LSP semantics as if they were MCP

## Why MCP is not “just LSP”

LSP and MCP both use JSON-RPC 2.0 and commonly run over framed stdio transports, but they solve different problems:

- LSP is document and editor oriented
- MCP is tool and resource oriented

AXSG therefore shares:

- message framing
- serialization options
- request and response helpers
- transport-neutral command routers

AXSG does not share:

- lifecycle assumptions
- method names
- editor-specific payloads

That keeps the remote stack reusable without forcing editor concepts onto MCP clients.

## Current adapter layout

| Surface | Adapter | Shared layer underneath |
| --- | --- | --- |
| Standalone language server | `XamlToCSharpGenerator.LanguageServer.Tool` | `RemoteProtocol.JsonRpc` plus shared query services |
| Workspace MCP host | `XamlToCSharpGenerator.McpServer.Tool` | `RemoteProtocol.JsonRpc` plus `McpServerCore` plus shared query services |
| Preview helper command transport | `PreviewHostCommandRouter` | shared preview payload contracts |
| Preview MCP host | `PreviewHostMcpServer` | shared preview payload contracts plus shared MCP core |
| Studio remote design server | `AxsgStudioRemoteCommandRouter` | shared runtime query services plus shared studio contracts |
| Runtime MCP host | `XamlSourceGenRuntimeMcpServer` | shared runtime query services plus shared MCP core |

## Shared packages

### `XamlToCSharpGenerator.RemoteProtocol`

This package is the reusable transport and contract layer. It contains:

- JSON-RPC framing helpers
- MCP server core
- preview host contracts
- studio remote contracts

It is the package to reuse when you need AXSG remote protocols without copying transport code.

### Shared operation services

The transport-neutral query layer now lives in services such as:

- `AxsgPreviewQueryService`
- `AxsgRuntimeQueryService`

Those are consumed by both LSP and MCP hosts so the same operation shape does not get reimplemented per adapter.

## MCP host split

AXSG intentionally has more than one MCP host because workspace state and live runtime state are not the same thing.

### Workspace MCP host

This host:

- runs independently as `axsg-mcp`
- sees files, projects, and workspace options
- is best for preview project-context resolution and general tooling queries

It does not automatically attach to a live Avalonia app.

### Runtime MCP host

This host:

- runs inside the Avalonia process
- sees hot reload, hot design, and studio state directly
- supports `resources/subscribe` and event-style resource updates

This is the host you want for `dotnet watch`, hot reload, hot design, and live-app inspection.

### Preview MCP host

This host:

- runs inside the preview helper process
- exposes explicit preview lifecycle and preview hot-reload tools/resources
- supports dynamic tool and resource catalogs as the active preview session starts or stops

This is the host you want when preview is the system under test.

## Why the VS Code extension still has its own runtime behavior

The VS Code extension is a product surface, not just a transport wrapper. It still owns:

- activation behavior
- lazy language-server startup
- VS Code preview webview integration
- editor middleware and inline-C# projection interop

The extension can reuse the shared remote-operation layer, but it still needs editor-specific orchestration that does not belong in MCP or LSP core contracts.

## Subscription model

AXSG now uses a capability-based model:

- workspace MCP host: query-oriented, poll after changes
- runtime MCP host: subscribe to status and event resources
- preview MCP host: subscribe to lifecycle resources and react to list-changed notifications

This split matches the real ownership model of the processes instead of pretending one host can answer every question equally well.

## Preview hot reload over MCP

Preview MCP now supports in-process live preview application directly through:

- `axsg.preview.hotReload`

That operation is intentionally different from the lower-level:

- `axsg.preview.update`

`hotReload` waits for the preview session to complete the in-process apply and returns the concrete result payload. `update` is still available for dispatch-only flows and compatibility with clients that already consume the lifecycle resources asynchronously.

This keeps the preview host aligned with the rest of AXSG’s shared remote-operation model:

- transport-neutral preview session/router underneath
- MCP adapter on top
- status and event resources as the subscription surface

## Related docs

- [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/)
- [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/)
- [Language Service and VS Code](language-service-and-vscode/)
- [Runtime and Hot Reload](runtime-and-hot-reload/)
- [Tooling Surface](../concepts/tooling-surface/)
- [Package: XamlToCSharpGenerator.RemoteProtocol](../reference/remote-protocol/)
