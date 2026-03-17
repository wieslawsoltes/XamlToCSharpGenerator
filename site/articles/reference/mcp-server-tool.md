---
title: "Package: XamlToCSharpGenerator.McpServer.Tool"
---

# XamlToCSharpGenerator.McpServer.Tool

## Role

The packaged `.NET` tool host for AXSG’s workspace MCP surface.

## Install

```bash
dotnet tool install --global XamlToCSharpGenerator.McpServer.Tool --version x.y.z
```

## Command

```bash
axsg-mcp --workspace /path/to/workspace
```

## Use it when

- you want MCP access to AXSG workspace queries outside VS Code
- you need preview project-context resolution for an AI or tool client
- you want a standalone MCP process instead of embedding the runtime host into the app

## What it exposes

The tool exposes a query-oriented MCP surface over the workspace and shared AXSG operation layer.

Primary tool surface:

- `axsg.preview.projectContext`
- `axsg.workspace.metadataDocument`
- `axsg.workspace.inlineCSharpProjections`
- `axsg.workspace.csharpReferences`
- `axsg.workspace.csharpDeclarations`
- `axsg.workspace.renamePropagation`
- `axsg.workspace.prepareRename`
- `axsg.workspace.rename`
- shared snapshot tools from the runtime catalog such as:
  - `axsg.hotReload.status`
  - `axsg.hotDesign.status`
  - `axsg.hotDesign.documents`
  - `axsg.hotDesign.workspace`
  - `axsg.studio.status`

Primary resources:

- `axsg://runtime/hotreload/status`
- `axsg://runtime/hotdesign/status`
- `axsg://runtime/hotdesign/documents`
- `axsg://runtime/studio/status`

Those resources use the same payload shape as the runtime-attached host, but on the standalone tool they are only snapshots from the `axsg-mcp` process itself. They are not subscriptions into a separate running Avalonia app.

For the dedicated workspace language-service tool guidance, see [Workspace MCP Language Tools](../guides/workspace-mcp-language-tools/).

## What it does not do

It does not automatically attach to a separate running Avalonia app.

That means:

- it is ideal for workspace and project queries
- it is not the live runtime host for `dotnet watch`
- it does not replace `XamlSourceGenRuntimeMcpServer`

For live runtime state, host the runtime MCP server in-process inside the application.

## Typical scenarios

- external AI agents that need AXSG project context
- preview launch planning before starting a preview session
- CI or local tooling that needs AXSG queries without an editor
- debugging shared AXSG MCP behavior independent from VS Code

## Related docs

- [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/)
- [Workspace MCP Language Tools](../guides/workspace-mcp-language-tools/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Tooling Stack Packages](tooling-stack/)
- [Artifact Matrix](artifact-matrix/)
