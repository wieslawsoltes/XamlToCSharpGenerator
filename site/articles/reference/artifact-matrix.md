---
title: "Artifact Matrix"
---

# Artifact Matrix

AXSG ships multiple artifacts because the project spans compile-time, runtime, editor, and release tooling.

## Artifact groups

- compiler and framework packages
- runtime packages
- language service and editor packages
- CLI tool package
- VS Code extension

## Recommended install surface

Use these first unless you are composing a custom toolchain:

| Artifact | Kind | Install |
| --- | --- | --- |
| `XamlToCSharpGenerator` | Application NuGet package | `dotnet add package XamlToCSharpGenerator` |
| `XamlToCSharpGenerator.LanguageServer.Tool` | .NET tool package | `dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool` |
| `AXSG XAML Language Service` | VS Code extension | `code --install-extension ./axsg-language-server-<version>.vsix` |

## Advanced composition

For custom compiler, runtime, or tooling integration, AXSG also ships:

- `XamlToCSharpGenerator.Build`
- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`
- `XamlToCSharpGenerator.LanguageService`
- `XamlToCSharpGenerator.Editor.Avalonia`
- `XamlToCSharpGenerator.Generator`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Framework.Abstractions`
- `XamlToCSharpGenerator.ExpressionSemantics`
- `XamlToCSharpGenerator.MiniLanguageParsing`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.NoUi`

For the full current matrix with badges and marketplace links, see the repository [README](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/README.md#what-ships).

For the docs-specific mapping between package identity, shipped payload, and generated API coverage, see [Package and Assembly](package-and-assembly/).
