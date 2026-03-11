---
title: "Package Guides"
---

# Package Guides

This section documents every shipped AXSG package, tool, and editor artifact. Use it when you need to choose the right install surface instead of starting from the raw namespace tree.

## Package map

| Artifact | Primary payload | Target | Generated API |
| --- | --- | --- | --- |
| `XamlToCSharpGenerator` | package shell for build/generator/runtime composition | app authors | narrative only |
| `XamlToCSharpGenerator.Build` | buildTransitive props/targets | SDK/build integrators | narrative only |
| `XamlToCSharpGenerator.Compiler` | `XamlToCSharpGenerator.Compiler.dll` | compiler/tool authors | yes |
| `XamlToCSharpGenerator.Core` | `XamlToCSharpGenerator.Core.dll` | compiler/tool authors | yes |
| `XamlToCSharpGenerator.Framework.Abstractions` | `XamlToCSharpGenerator.Framework.Abstractions.dll` | framework profile authors | yes |
| `XamlToCSharpGenerator.Avalonia` | `XamlToCSharpGenerator.Avalonia.dll` | Avalonia compiler profile authors | yes |
| `XamlToCSharpGenerator.ExpressionSemantics` | `XamlToCSharpGenerator.ExpressionSemantics.dll` | expression-analysis/tooling authors | yes |
| `XamlToCSharpGenerator.MiniLanguageParsing` | `XamlToCSharpGenerator.MiniLanguageParsing.dll` | parser/tooling authors | yes |
| `XamlToCSharpGenerator.NoUi` | `XamlToCSharpGenerator.NoUi.dll` | framework-neutral profile experiments | yes |
| `XamlToCSharpGenerator.Generator` | analyzer/source generator payload | advanced generator consumers | yes |
| `XamlToCSharpGenerator.Runtime` | runtime umbrella composition | app/runtime consumers | narrative only |
| `XamlToCSharpGenerator.Runtime.Core` | `XamlToCSharpGenerator.Runtime.Core.dll` | framework-neutral runtime consumers | yes |
| `XamlToCSharpGenerator.Runtime.Avalonia` | `XamlToCSharpGenerator.Runtime.Avalonia.dll` | Avalonia runtime consumers | yes |
| `XamlToCSharpGenerator.LanguageService` | `XamlToCSharpGenerator.LanguageService.dll` | editor/tool authors | yes |
| `XamlToCSharpGenerator.LanguageServer.Tool` | `axsg-lsp` global/local tool command | LSP hosts | yes |
| `XamlToCSharpGenerator.Editor.Avalonia` | `XamlToCSharpGenerator.Editor.Avalonia.dll` | in-app editor hosts | yes |
| `xamltocsharpgenerator.axsg-language-server` | `.vsix` extension bundle | VS Code users | narrative only |

## App-facing install surfaces

- [XamlToCSharpGenerator](xamltocsharpgenerator/)
- [XamlToCSharpGenerator.Runtime](runtime/)
- [VS Code Extension](vscode-extension/)

## Build and compiler packages

- [XamlToCSharpGenerator.Build](build/)
- [XamlToCSharpGenerator.Compiler](compiler/)
- [XamlToCSharpGenerator.Core](core/)
- [XamlToCSharpGenerator.Framework.Abstractions](framework-abstractions/)
- [XamlToCSharpGenerator.Avalonia](avalonia/)
- [XamlToCSharpGenerator.NoUi](noui/)
- [XamlToCSharpGenerator.Generator](generator/)

## Parsing and expression packages

- [XamlToCSharpGenerator.ExpressionSemantics](expression-semantics/)
- [XamlToCSharpGenerator.MiniLanguageParsing](mini-language-parsing/)

## Runtime and tooling packages

- [XamlToCSharpGenerator.Runtime.Core](runtime-core/)
- [XamlToCSharpGenerator.Runtime.Avalonia](runtime-avalonia/)
- [XamlToCSharpGenerator.LanguageService](language-service/)
- [XamlToCSharpGenerator.LanguageServer.Tool](language-server-tool/)
- [XamlToCSharpGenerator.Editor.Avalonia](editor-avalonia/)

Related:

- [Package and Assembly](package-and-assembly/)
- [Package Catalog](package-catalog/)
- [API Navigation Guide](api-navigation-guide/)
- [API Coverage Index](api-coverage-index/)
- [Artifact Matrix](artifact-matrix/)
