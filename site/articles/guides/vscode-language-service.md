---
title: "VS Code and Language Service"
---

# VS Code and Language Service

This guide explains how the AXSG language service, the standalone server, and the VS Code extension fit together. Use it when editor behavior is the problem you are trying to understand, not build output.

For the product-facing extension docs, including installation, configuration, preview, and the AXSG Inspector side panel, start with the dedicated [VS Code](../vscode/) section.

## Main components

The editing stack is split into three parts:

1. `XamlToCSharpGenerator.LanguageService`
   - semantic engine for completion, hover, navigation, references, rename, inlay hints, and semantic tokens
2. `XamlToCSharpGenerator.LanguageServer.Tool`
   - standalone LSP host
3. `wieslawsoltes.axsg-language-server`
   - VS Code extension that hosts the client, starts the server lazily, and adds editor-specific behaviors

## Request flow

For normal XAML requests, the stack works like this:

1. the extension activates for XAML-capable workspaces
2. the language client starts lazily on the first server-backed request
3. the language server forwards the request into `XamlToCSharpGenerator.LanguageService`
4. the language service loads project/compilation state only when the request actually needs it
5. the result is mapped back into editor-friendly LSP payloads

That separation matters when debugging startup cost, first-request latency, or differences between build semantics and editor semantics.

## What is supported

The extension and server cover:

- compiled bindings, runtime bindings, and shorthand expressions
- inline C# and CDATA code blocks
- selector tokens such as style classes, pseudoclasses, and `#name`
- `xmlns` prefixes and include URIs
- cross-language navigation between XAML and C#

More concretely, that includes:

- qualified element prefixes vs type-name navigation
- property elements and owner-qualified property tokens
- template bindings and resource keys
- C#-driven rename propagation into XAML for supported symbols
- inline-C# completion, hover, references, and projected-C# interop where it is beneficial

## Startup and performance

The current implementation defers heavy workspace construction until the first real semantic request.

That means:

- opening arbitrary C# files does not eagerly start the AXSG server
- inline C# interop only falls back to projected C# provider calls when AXSG cannot answer directly
- normal XAML navigation is not forced through projected C# documents

If performance is poor, separate the issue into:

- extension activation and first usable request
- first compilation-backed request
- repeated warm requests
- projected inline-C# interop requests

Those are different paths with different caches and different fallback behavior.

## Inline C# interop boundary

The extension can reuse existing C# editor providers for:

- completion
- hover
- definition/declaration
- references

AXSG still owns semantic coloring inside XAML because the external C# semantic-token legend cannot be safely remapped into the XAML document legend.

That boundary is deliberate. AXSG remains the semantic owner for the XAML document. Projected C# provider reuse is opportunistic and only used where it materially improves the editing experience without taking control away from XAML semantics.

## Typical debugging workflow

When editor behavior looks wrong, use this order:

1. confirm the project builds
2. confirm AXSG diagnostics in the XAML file are sane
3. reload the VS Code window or restart the AXSG language server
4. isolate whether the issue is:
   - compiler semantics
   - language-service semantics
   - VS Code extension middleware
   - projected inline-C# fallback

This keeps you from treating every editor issue as a VS Code integration bug when the root cause often lives earlier.

## Choosing the right host

Prefer the bundled VS Code extension when you want:

- the full client/server pairing
- AXSG-first handling for XAML-native semantics
- inline-C# projection and editor middleware already wired
- built-in preview orchestration and preview-webview management

Prefer the standalone language-server tool when you want:

- another editor integration
- direct process control over the server
- transport/protocol debugging independent of VS Code

Prefer the MCP host when you want:

- workspace queries from an AI client or external tool
- preview project-context resolution without talking to VS Code
- runtime or preview state through the dedicated runtime or preview MCP hosts

The extension still uses its own bundled preview orchestration for the product preview experience. MCP support exists alongside that flow, not instead of it.

## Related docs

- [VS Code](../vscode/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- [MCP Servers and Live Tooling](mcp-servers-and-live-tooling/)
- [Preview MCP Host and Live Preview](preview-mcp-host-and-live-preview/)
- [Navigation and Refactorings](navigation-and-refactorings/)
- [Inline C# Code](inline-csharp-code/)
- [Troubleshooting](troubleshooting/)
