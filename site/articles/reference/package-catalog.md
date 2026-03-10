---
title: "Package Catalog"
---

# Package Catalog

This page covers the full shipped package set and when to use each artifact. The linked package guides go deeper and point at the correct narrative/API entry points.

| Package | Role | Use it when | Guide |
| --- | --- | --- |
| `XamlToCSharpGenerator` | umbrella package | you want the standard app-facing install surface | [Guide](packages/xamltocsharpgenerator) |
| `XamlToCSharpGenerator.Build` | MSBuild props/targets | you need explicit build integration hooks | [Guide](packages/build) |
| `XamlToCSharpGenerator.Compiler` | framework-agnostic host | you are composing your own profile/tooling pipeline | [Guide](packages/compiler) |
| `XamlToCSharpGenerator.Core` | contracts/models | you need parser and semantic model contracts | [Guide](packages/core) |
| `XamlToCSharpGenerator.Framework.Abstractions` | profile contracts | you are implementing/extending a framework profile | [Guide](packages/framework-abstractions) |
| `XamlToCSharpGenerator.Avalonia` | Avalonia binder/emitter | you want the Avalonia-specific compiler profile | [Guide](packages/avalonia) |
| `XamlToCSharpGenerator.ExpressionSemantics` | Roslyn expression analysis | you need reusable expression/lambda analysis | [Guide](packages/expression-semantics) |
| `XamlToCSharpGenerator.MiniLanguageParsing` | low-allocation tokenizers | you need selector/binding/markup parsing utilities | [Guide](packages/mini-language-parsing) |
| `XamlToCSharpGenerator.NoUi` | framework-neutral pilot profile | you are testing host reuse without Avalonia | [Guide](packages/noui) |
| `XamlToCSharpGenerator.Generator` | standalone generator backend | you need the generator assembly directly | [Guide](packages/generator) |
| `XamlToCSharpGenerator.Runtime` | runtime umbrella | you want runtime packages without picking sublayers | [Guide](packages/runtime) |
| `XamlToCSharpGenerator.Runtime.Core` | framework-neutral runtime contracts | you need registry/source-info/hot-reload contracts | [Guide](packages/runtime-core) |
| `XamlToCSharpGenerator.Runtime.Avalonia` | Avalonia runtime integration | you need Avalonia loader/bootstrap/hot reload behavior | [Guide](packages/runtime-avalonia) |
| `XamlToCSharpGenerator.LanguageService` | shared LS core | you are embedding or hosting AXSG language features | [Guide](packages/language-service) |
| `XamlToCSharpGenerator.LanguageServer.Tool` | `dotnet tool` LSP host | you want the standalone language server | [Guide](packages/language-server-tool) |
| `XamlToCSharpGenerator.Editor.Avalonia` | AvaloniaEdit-based editor | you need an in-app AXAML editor control | [Guide](packages/editor-avalonia) |
| `xamltocsharpgenerator.axsg-language-server` | VS Code extension | you want the bundled VS Code client/server experience | [Guide](packages/vscode-extension) |

Related:

- [Artifact Matrix](artifact-matrix)
- [Internal Support Components](internal-support-components)
- [Package Guides](packages/readme)
- [Namespace Language Service and Tooling](namespace-language-service-and-tooling)
