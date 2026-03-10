---
title: "Artifact: VS Code Extension"
---

# VS Code Extension

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

## How it fits with the rest of the stack

The extension is a thin client plus editor middleware over:

- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.LanguageService`

It also owns editor-specific concerns such as activation strategy, virtual-document projections, and client-side provider fallbacks.

## Related docs

- [Language Service and VS Code](../../architecture/language-service-and-vscode/)
- [VS Code and Language Service](../guides/vscode-language-service/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
- [Artifact Matrix](artifact-matrix/)
