---
title: "Tooling Surface"
---

# Tooling Surface

AXSG ships more than a source generator. The tooling surface is split so editor and automation scenarios can reuse the same semantic model.

## Main tooling packages

- `XamlToCSharpGenerator.LanguageService`
- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.Editor.Avalonia`
- VS Code extension `xamltocsharpgenerator.axsg-language-server`

Related internal support component:

- `XamlToCSharpGenerator.DotNetWatch.Proxy` for the dotnet-watch named-pipe bridge used by the mobile/watch hot reload path

## Shared semantics

The language service uses the same compiler and framework semantic model to provide:

- completion
- hover
- definitions/declarations/references
- rename/refactoring propagation
- semantic tokens and inlay hints
- inline C# and C#-expression support inside XAML
