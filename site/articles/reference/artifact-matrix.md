---
title: "Artifact Matrix"
---

# Artifact Matrix

AXSG ships multiple artifacts because the project spans compile-time, runtime, editor, and release tooling.

## Artifact groups

- compiler and framework packages
- runtime packages
- language service and editor packages
- CLI tool packages
- internal host artifacts
- VS Code extension

## Recommended install surface

Use these first unless you are composing a custom toolchain:

| Artifact | Kind | Install |
| --- | --- | --- |
| `XamlToCSharpGenerator` | Application NuGet package | `dotnet add package XamlToCSharpGenerator` |
| `XamlToCSharpGenerator.LanguageServer.Tool` | .NET tool package | `dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool` |
| `XamlToCSharpGenerator.McpServer.Tool` | .NET tool package | `dotnet tool install --global XamlToCSharpGenerator.McpServer.Tool` |
| `AXSG XAML Language Service` | VS Code extension | `code --install-extension ./axsg-language-server-x.y.z.vsix` |

## Advanced composition

For custom compiler, runtime, or tooling integration, AXSG also ships:

- `XamlToCSharpGenerator.Build`
- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`
- `XamlToCSharpGenerator.RemoteProtocol`
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

Operational host artifacts that are important but not shipped as standalone packages include:

- `XamlToCSharpGenerator.PreviewerHost`
- `XamlToCSharpGenerator.Previewer.DesignerHost`

Use [Artifact: XamlToCSharpGenerator.PreviewerHost](preview-host/) when the system you need to control is preview itself.

For the full current matrix with badges and marketplace links, see the repository [README](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/README.md#what-ships).

For the docs-specific mapping between package identity, shipped payload, and generated API coverage, see [Package and Assembly](package-and-assembly/).
