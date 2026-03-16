---
title: "Package: XamlToCSharpGenerator.RemoteProtocol"
---

# XamlToCSharpGenerator.RemoteProtocol

## Role

Shared JSON-RPC framing and AXSG remote protocol contracts for LSP, MCP, preview, and studio/runtime hosts.

## Related namespaces

- <xref:XamlToCSharpGenerator.RemoteProtocol.JsonRpc>
- <xref:XamlToCSharpGenerator.RemoteProtocol.Mcp>
- <xref:XamlToCSharpGenerator.RemoteProtocol.Preview>
- <xref:XamlToCSharpGenerator.RemoteProtocol.Studio>

## Use it when

- you are building a custom AXSG MCP or JSON-RPC host
- you want shared framing and helpers instead of duplicating transport code
- you are integrating preview or studio remote contracts outside the built-in hosts

## Major responsibilities

This package owns:

- shared JSON-RPC message reader and writer infrastructure
- MCP server core and capability handling
- preview host request, response, event, and in-process hot-reload payload contracts
- studio remote request and response payload contracts

It exists specifically so AXSG does not have to reimplement transport and payload plumbing independently for LSP, MCP, preview, and runtime hosts.

## Typical consumers

- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.McpServer.Tool`
- `XamlToCSharpGenerator.PreviewerHost`
- `XamlToCSharpGenerator.Runtime.Avalonia`

## Related docs

- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/)
- [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/)
- [Artifact: XamlToCSharpGenerator.PreviewerHost](preview-host/)
- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/)
- [Tooling Stack Packages](tooling-stack/)
