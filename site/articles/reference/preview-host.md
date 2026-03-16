---
title: "Artifact: XamlToCSharpGenerator.PreviewerHost"
---

# XamlToCSharpGenerator.PreviewerHost

## Role

The preview helper and preview MCP host for AXSG preview orchestration.

This repo component is not a shipped global tool or public NuGet package. It is an operational host artifact used by:

- the VS Code extension preview workflow
- custom preview clients
- automated preview smoke tests
- MCP-based preview orchestration

## Run modes

`XamlToCSharpGenerator.PreviewerHost` supports two modes:

- lightweight helper mode over the legacy JSON-line command transport
- MCP mode over JSON-RPC stdio via `--mcp`

Preview MCP mode:

```bash
dotnet run --project src/XamlToCSharpGenerator.PreviewerHost -- --mcp
```

## What it owns

This host owns preview-session orchestration, not workspace analysis and not general runtime hot reload hosting.

It is responsible for:

- starting preview sessions
- relaying preview URL/session metadata
- pushing live XAML updates into the active preview session
- exposing preview lifecycle state over MCP
- exposing in-process preview hot reload completion through `axsg.preview.hotReload`

It uses the shared contracts from `XamlToCSharpGenerator.RemoteProtocol.Preview`, so MCP mode and the lightweight helper transport share the same normalized preview operation layer.

## MCP surface

When started with `--mcp`, the host exposes:

### Tools

- `axsg.preview.start`
- `axsg.preview.hotReload`
- `axsg.preview.update`
- `axsg.preview.stop`

`axsg.preview.hotReload` is the preferred mutation tool when the caller needs the in-process live-apply result. `axsg.preview.update` is the lower-level dispatch-only operation.

### Resources

- `axsg://preview/session/status`
- `axsg://preview/session/events`
- dynamic `axsg://preview/session/current`

### Dynamic behavior

The host advertises:

- `notifications/tools/list_changed`
- `notifications/resources/list_changed`
- `resources/subscribe`
- `notifications/resources/updated`

That dynamic behavior matters because the active preview session changes the available tool/resource catalog.

## What it is not

It is not:

- the standalone workspace MCP host
- the runtime MCP host inside a watched Avalonia app
- the designer host that loads the previewed app itself

Use:

- `XamlToCSharpGenerator.McpServer.Tool` for workspace MCP queries
- `XamlSourceGenRuntimeMcpServer` for live runtime hot reload, hot design, and studio state
- `XamlToCSharpGenerator.Previewer.DesignerHost` as the internal designer-process layer that actually hosts source-generated preview runtime loading

## Internal layering

The preview pipeline is split intentionally:

- `XamlToCSharpGenerator.PreviewerHost`
  - outer helper process
  - MCP or lightweight helper transport
  - preview session lifecycle and transport orchestration
- `XamlToCSharpGenerator.Previewer.DesignerHost`
  - inner designer host
  - source-generated runtime loader integration
  - live preview overlay application

This separation lets AXSG reuse the same preview session layer across the VS Code extension, MCP clients, and test harnesses.

## Use it when

- you are implementing a custom AXSG preview client
- you need preview lifecycle resources over MCP
- you need in-process preview hot reload completion through MCP
- you are testing preview start/update/stop behavior independently from VS Code

## Related docs

- [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/)
- [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Package: XamlToCSharpGenerator.RemoteProtocol](remote-protocol/)
- [Tooling Stack Packages](tooling-stack/)
