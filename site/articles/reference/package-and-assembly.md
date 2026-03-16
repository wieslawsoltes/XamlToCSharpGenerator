---
title: "Package and Assembly"
---

# Package and Assembly

AXSG ships a mix of package shells, code-bearing libraries, build payloads, a .NET tool, and a VS Code extension. This page maps each install identity to its primary shipped payload and generated API coverage.

When you already know the assembly or tool package name and need the direct generated API route, use [Assembly Catalog](assembly-catalog/).

## Distribution overview

| Artifact | Kind | Primary payload | Target framework / host | Generated API |
| --- | --- | --- | --- | --- |
| `XamlToCSharpGenerator` | NuGet package | umbrella package shell that composes build/generator/runtime assets | NuGet package | narrative only |
| `XamlToCSharpGenerator.Build` | NuGet package | `buildTransitive/XamlToCSharpGenerator.Build.props` and `.targets` | buildTransitive | narrative only |
| `XamlToCSharpGenerator.Compiler` | NuGet package | `XamlToCSharpGenerator.Compiler.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.Core` | NuGet package | `XamlToCSharpGenerator.Core.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.Framework.Abstractions` | NuGet package | `XamlToCSharpGenerator.Framework.Abstractions.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.Avalonia` | NuGet package | `XamlToCSharpGenerator.Avalonia.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.ExpressionSemantics` | NuGet package | `XamlToCSharpGenerator.ExpressionSemantics.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.MiniLanguageParsing` | NuGet package | `XamlToCSharpGenerator.MiniLanguageParsing.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.NoUi` | NuGet package | `XamlToCSharpGenerator.NoUi.dll` | `netstandard2.0` | yes |
| `XamlToCSharpGenerator.Generator` | NuGet package | analyzer/source generator payload | analyzer payload (`netstandard2.0`) | yes |
| `XamlToCSharpGenerator.Runtime` | NuGet package | runtime umbrella composition | NuGet package | narrative only |
| `XamlToCSharpGenerator.Runtime.Core` | NuGet package | `XamlToCSharpGenerator.Runtime.Core.dll` | `net10.0` | yes |
| `XamlToCSharpGenerator.Runtime.Avalonia` | NuGet package | `XamlToCSharpGenerator.Runtime.Avalonia.dll` | `net10.0` | yes |
| `XamlToCSharpGenerator.RemoteProtocol` | NuGet package | `XamlToCSharpGenerator.RemoteProtocol.dll` | `net6.0`, `net8.0`, `net10.0` | yes |
| `XamlToCSharpGenerator.LanguageService` | NuGet package | `XamlToCSharpGenerator.LanguageService.dll` | `net10.0` | yes |
| `XamlToCSharpGenerator.LanguageServer.Tool` | .NET tool package | `axsg-lsp` command over `XamlToCSharpGenerator.LanguageServer.dll` | `net10.0` tool host | yes |
| `XamlToCSharpGenerator.McpServer.Tool` | .NET tool package | `axsg-mcp` command over `XamlToCSharpGenerator.McpServer.dll` | `net10.0` tool host | narrative only |
| `XamlToCSharpGenerator.Editor.Avalonia` | NuGet package | `XamlToCSharpGenerator.Editor.Avalonia.dll` | `net10.0` | yes |
| `xamltocsharpgenerator.axsg-language-server` | VS Code extension | VSIX bundle with JS client and managed AXSG server | VS Code | narrative only |

## Why some artifacts are narrative-only

Three shipped package IDs are intentionally documented only through narrative pages instead of generated API:

- `XamlToCSharpGenerator`
  - this is the application-facing package shell
  - its value is composition, not a standalone code API
- `XamlToCSharpGenerator.Build`
  - this ships MSBuild props/targets
  - generated namespace pages from this project only expose assembly attributes and do not help users
- `XamlToCSharpGenerator.Runtime`
  - this composes the runtime layers for app authors
  - the useful public API lives in `Runtime.Core` and `Runtime.Avalonia`

These packages are still fully documented through:

- [Package Catalog](package-catalog/)
- [Package Guides](package-guides/)
- [Artifact Matrix](artifact-matrix/)

## Internal support executable

One repo component is important operationally but is not a shipped package:

| Component | Role | Docs |
| --- | --- | --- |
| `XamlToCSharpGenerator.DotNetWatch.Proxy` | `dotnet watch` / hot-reload proxy process | [Internal Support Components](internal-support-components/) |
| `XamlToCSharpGenerator.PreviewerHost` | preview helper host and preview MCP host | [Artifact: XamlToCSharpGenerator.PreviewerHost](preview-host/), [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/) |

## Guidance

- Use [Package Catalog](package-catalog/) to choose the right artifact.
- Use [API Coverage Index](api-coverage-index/) to jump into the generated API and namespace summaries.
- Use [Package Guides](package-guides/) when you need install guidance and package-specific narrative.
