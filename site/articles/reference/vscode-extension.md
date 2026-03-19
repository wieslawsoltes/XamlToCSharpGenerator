---
title: "Artifact: VS Code Extension"
---

# VS Code Extension

For end-user setup, configuration, preview, and inspector workflows, use the dedicated [VS Code](../vscode/) section. This page stays package/artifact-oriented.

## Role

The packaged VS Code extension bundles the AXSG language client, launches the server, and integrates XAML plus inline-C# editing features into VS Code.

## Use it when

- you are editing AXAML/XAML in VS Code
- you want bundled setup instead of wiring the standalone tool manually
- you need XAML-aware navigation and semantic services that follow AXSG compiler rules

## Features

- completion, hover, definition, declaration, references, rename, and semantic tokens
- inline C# editor support
- cross-language navigation between XAML and C#
- projected C# interop where existing editor-side C# providers add value
- Avalonia preview with AXSG source-generated and Avalonia/XamlX modes

## How it fits with the rest of the stack

The extension is a thin client plus editor middleware over:

- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.LanguageService`

It also owns editor-specific concerns such as activation strategy, virtual-document projections, client-side provider fallbacks, and the bundled preview webview workflow.

The extension bundles and launches the preview helper automatically for the normal product workflow. The same helper also has an MCP mode for custom clients and test harnesses, but the extension preview UI does not require you to start an MCP server manually.

The preview MCP host behind that helper now supports explicit in-process preview hot reload through `axsg.preview.hotReload`, but that remains a custom-client surface rather than the packaged extension UX.

## Related docs

- [VS Code](../vscode/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- [VS Code and Language Service](../guides/vscode-language-service/)
- [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/)
- [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
- [Artifact Matrix](artifact-matrix/)
