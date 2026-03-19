---
title: "Package Catalog"
---

# Package Catalog

This page covers the full shipped package set and when to use each artifact. The linked package guides go deeper and point at the correct narrative/API entry points.

Use [Package and Assembly](package-and-assembly/) when you need the concrete mapping from install identity to runtime assembly, targets payload, tool command, or generated API coverage.

| Package | Role | Use it when | Guide |
| --- | --- | --- |
| `XamlToCSharpGenerator` | umbrella package | you want the standard app-facing install surface | [Guide](xamltocsharpgenerator/) |
| `XamlToCSharpGenerator.Build` | MSBuild props/targets | you need explicit build integration hooks | [Guide](build/) |
| `XamlToCSharpGenerator.Compiler` | framework-agnostic host | you are composing your own profile/tooling pipeline | [Guide](compiler/) |
| `XamlToCSharpGenerator.Core` | contracts/models | you need parser and semantic model contracts | [Guide](core/) |
| `XamlToCSharpGenerator.Framework.Abstractions` | profile contracts | you are implementing/extending a framework profile | [Guide](framework-abstractions/) |
| `XamlToCSharpGenerator.Avalonia` | Avalonia binder/emitter | you want the Avalonia-specific compiler profile | [Guide](avalonia/) |
| `XamlToCSharpGenerator.ExpressionSemantics` | Roslyn expression analysis | you need reusable expression/lambda analysis | [Guide](expression-semantics/) |
| `XamlToCSharpGenerator.MiniLanguageParsing` | low-allocation tokenizers | you need selector/binding/markup parsing utilities | [Guide](mini-language-parsing/) |
| `XamlToCSharpGenerator.NoUi` | framework-neutral pilot profile | you are testing host reuse without Avalonia | [Guide](noui/) |
| `XamlToCSharpGenerator.Generator` | standalone generator backend | you need the generator assembly directly | [Guide](generator/) |
| `XamlToCSharpGenerator.Runtime` | runtime umbrella | you want runtime packages without picking sublayers | [Guide](runtime/) |
| `XamlToCSharpGenerator.Runtime.Core` | framework-neutral runtime contracts | you need registry/source-info/hot-reload contracts | [Guide](runtime-core/) |
| `XamlToCSharpGenerator.Runtime.Avalonia` | Avalonia runtime integration | you need Avalonia loader/bootstrap/hot reload behavior | [Guide](runtime-avalonia/) |
| `XamlToCSharpGenerator.RemoteProtocol` | shared remote protocol contracts | you are composing MCP, preview, or remote AXSG hosts | [Guide](remote-protocol/) |
| `XamlToCSharpGenerator.LanguageService` | shared LS core | you are embedding or hosting AXSG language features | [Guide](language-service/) |
| `XamlToCSharpGenerator.LanguageServer.Tool` | `dotnet tool` LSP host | you want the standalone language server | [Guide](language-server-tool/) |
| `XamlToCSharpGenerator.McpServer.Tool` | `dotnet tool` MCP host | you want the standalone workspace MCP server | [Guide](mcp-server-tool/) |
| `XamlToCSharpGenerator.Editor.Avalonia` | AvaloniaEdit-based editor | you need an in-app AXAML editor control | [Guide](editor-avalonia/) |
| `wieslawsoltes.axsg-language-server` | VS Code extension | you want the bundled VS Code client/server experience | [Guide](vscode-extension/) |

Related:

- [Package and Assembly](package-and-assembly/)
- [Artifact Matrix](artifact-matrix/)
- [Internal Support Components](internal-support-components/)
- [Package Guides](package-guides/)
- [Namespace Language Service and Tooling](namespace-language-service-and-tooling/)
