---
title: "VS Code and Language Service"
---

# VS Code and Language Service

This guide explains how the AXSG language service and the VS Code extension fit together.

## Main components

The editing stack is split into three parts:

1. `XamlToCSharpGenerator.LanguageService`
   - semantic engine for completion, hover, navigation, references, rename, inlay hints, and semantic tokens
2. `XamlToCSharpGenerator.LanguageServer.Tool`
   - standalone LSP host
3. `xamltocsharpgenerator.axsg-language-server`
   - VS Code extension that hosts the client, starts the server lazily, and adds editor-specific behaviors

## What is supported

The extension and server cover:

- compiled bindings, runtime bindings, and shorthand expressions
- inline C# and CDATA code blocks
- selector tokens such as style classes, pseudoclasses, and `#name`
- `xmlns` prefixes and include URIs
- cross-language navigation between XAML and C#

## Startup and performance

The current implementation defers heavy workspace construction until the first real semantic request.

That means:

- opening arbitrary C# files does not eagerly start the AXSG server
- inline C# interop only falls back to projected C# provider calls when AXSG cannot answer directly
- normal XAML navigation is not forced through projected C# documents

## Inline C# interop boundary

The extension can reuse existing C# editor providers for:

- completion
- hover
- definition/declaration
- references

AXSG still owns semantic coloring inside XAML because the external C# semantic-token legend cannot be safely remapped into the XAML document legend.

## Related docs

- [Language Service and VS Code](../architecture/language-service-and-vscode)
- [Navigation and Refactorings](navigation-and-refactorings)
- [Inline C# Code](inline-csharp-code)
