---
title: "Package: XamlToCSharpGenerator.LanguageServer.Tool"
---

# XamlToCSharpGenerator.LanguageServer.Tool

## Role

The packaged `dotnet tool` host for the AXSG language server.

## Install

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool --version <VERSION>
```

## Use it when

- you want the standalone LSP host outside the bundled VS Code extension
- you are wiring editor integrations yourself
- you want to debug the server independently from client middleware

## What it exposes

The tool packages the AXSG server process and gives editors a stable command-oriented deployment path instead of requiring them to embed the shared engine directly.

It is the correct artifact when you want a reusable LSP host rather than a bundled editor experience.

## Typical scenarios

- local editor experimentation without the bundled VS Code extension
- alternative editor integrations that speak standard LSP
- automated editor or language-service smoke testing
- debugging transport, request handling, or project-loading behavior outside the VS Code client

## How it differs from the VS Code extension

The tool gives you the server process and transport surface.
It does not give you:

- VS Code activation rules
- status bar integration
- projected inline-C# middleware
- editor-specific fallback behavior

Those remain in the VS Code extension because they are client responsibilities.

## Related docs

- [Tooling Surface](../concepts/tooling-surface/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- [VS Code and Language Service](../guides/vscode-language-service/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
