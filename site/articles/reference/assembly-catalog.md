---
title: "Assembly Catalog"
---

# Assembly Catalog

This page maps every shipped AXSG artifact to its implementation assembly, generated API route, and the narrative pages that explain how to use it.

Use this page when you know the package or assembly name already and need the fastest route to the right generated API or guide.

## Code-bearing assemblies with generated API

| Artifact | Primary assembly | Generated API | Primary narrative docs |
| --- | --- | --- | --- |
| `XamlToCSharpGenerator.Compiler` | `XamlToCSharpGenerator.Compiler.dll` | `/api/XamlToCSharpGenerator.Compiler/index.html` | [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/), [Compiler and Core Namespaces](namespace-compiler-and-core/) |
| `XamlToCSharpGenerator.Core` | `XamlToCSharpGenerator.Core.dll` | `/api/XamlToCSharpGenerator.Core/index.html` | [Binding and Expression Model](../concepts/binding-and-expression-model/), [Compiler and Core Namespaces](namespace-compiler-and-core/) |
| `XamlToCSharpGenerator.Framework.Abstractions` | `XamlToCSharpGenerator.Framework.Abstractions.dll` | `/api/XamlToCSharpGenerator.Framework.Abstractions/index.html` | [Custom Framework Profiles](../advanced/custom-framework-profiles/), [Compiler and Core Namespaces](namespace-compiler-and-core/) |
| `XamlToCSharpGenerator.Avalonia` | `XamlToCSharpGenerator.Avalonia.dll` | `/api/XamlToCSharpGenerator.Avalonia.Binding/index.html` | [Styles, Templates, and Themes](../xaml/styles-templates-and-themes/), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/) |
| `XamlToCSharpGenerator.ExpressionSemantics` | `XamlToCSharpGenerator.ExpressionSemantics.dll` | `/api/XamlToCSharpGenerator.ExpressionSemantics/index.html` | [C# Expressions](../xaml/csharp-expressions/), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/) |
| `XamlToCSharpGenerator.MiniLanguageParsing` | `XamlToCSharpGenerator.MiniLanguageParsing.dll` | `/api/XamlToCSharpGenerator.MiniLanguageParsing/index.html` | [Resources, Includes, and URIs](../xaml/resources-includes-and-uris/), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/) |
| `XamlToCSharpGenerator.NoUi` | `XamlToCSharpGenerator.NoUi.dll` | `/api/XamlToCSharpGenerator.NoUi/index.html` | [Custom Framework Profiles](../advanced/custom-framework-profiles/), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/) |
| `XamlToCSharpGenerator.Generator` | `XamlToCSharpGenerator.Generator.dll` | `/api/XamlToCSharpGenerator.Generator/index.html` | [Compiler Pipeline](../architecture/compiler-pipeline/), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/) |
| `XamlToCSharpGenerator.Runtime.Core` | `XamlToCSharpGenerator.Runtime.Core.dll` | `/api/XamlToCSharpGenerator.Runtime.Core/index.html` | [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/), [Runtime and Editor Namespaces](namespace-runtime-and-editor/) |
| `XamlToCSharpGenerator.Runtime.Avalonia` | `XamlToCSharpGenerator.Runtime.Avalonia.dll` | `/api/XamlToCSharpGenerator.Runtime.Avalonia/index.html` | [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/), [Runtime and Editor Namespaces](namespace-runtime-and-editor/) |
| `XamlToCSharpGenerator.RemoteProtocol` | `XamlToCSharpGenerator.RemoteProtocol.dll` | `/api/XamlToCSharpGenerator.RemoteProtocol/index.html` | [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/), [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/) |
| `XamlToCSharpGenerator.LanguageService` | `XamlToCSharpGenerator.LanguageService.dll` | `/api/XamlToCSharpGenerator.LanguageService/index.html` | [VS Code and Language Service](../guides/vscode-language-service/), [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/) |
| `XamlToCSharpGenerator.LanguageServer.Tool` | `XamlToCSharpGenerator.LanguageServer.dll` | guide-first; no generated namespace landing page | [VS Code and Language Service](../guides/vscode-language-service/), [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/), [Package: XamlToCSharpGenerator.LanguageServer.Tool](language-server-tool/) |
| `XamlToCSharpGenerator.Editor.Avalonia` | `XamlToCSharpGenerator.Editor.Avalonia.dll` | `/api/XamlToCSharpGenerator.Editor.Avalonia/index.html` | [VS Code and Language Service](../guides/vscode-language-service/), [Runtime and Editor Namespaces](namespace-runtime-and-editor/) |

## Narrative-only shipped artifacts

These shipped artifacts are documented intentionally through package guides and reference pages rather than generated API, because their value is composition, packaging, or build integration rather than a stable public namespace surface.

| Artifact | Why it is narrative-only | Primary docs |
| --- | --- | --- |
| `XamlToCSharpGenerator` | umbrella package shell for the default install story | [Package: XamlToCSharpGenerator](xamltocsharpgenerator/), [Package and Assembly](package-and-assembly/) |
| `XamlToCSharpGenerator.Build` | buildTransitive props/targets package, not an API-first library | [Package: XamlToCSharpGenerator.Build](build/), [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules/) |
| `XamlToCSharpGenerator.Runtime` | compatibility package that composes runtime layers | [Package: XamlToCSharpGenerator.Runtime](runtime/), [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/) |
| `XamlToCSharpGenerator.McpServer.Tool` | `.NET` tool packaging for the workspace MCP host | [Package: XamlToCSharpGenerator.McpServer.Tool](mcp-server-tool/), [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/) |
| `wieslawsoltes.axsg-language-server` | VS Code extension package containing the JS client and managed server bundle | [Package: VS Code Extension](vscode-extension/), [VS Code and Language Service](../guides/vscode-language-service/) |

## Internal support components

These repo components matter operationally but are not standalone shipped packages:

| Component | Role | Primary docs |
| --- | --- | --- |
| `XamlToCSharpGenerator.DotNetWatch.Proxy` | `dotnet watch` bridge for hot reload on mobile/remote targets | [Internal Support Components](internal-support-components/), [Hot Reload and Hot Design](../guides/hot-reload-and-hot-design/) |
| `XamlToCSharpGenerator.PreviewerHost` | preview helper process and preview MCP host for explicit preview session control | [Artifact: XamlToCSharpGenerator.PreviewerHost](preview-host/), [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/) |

## Related

- [Package and Assembly](package-and-assembly/)
- [API Coverage Index](api-coverage-index/)
- [Package Catalog](package-catalog/)
- [Package Guides](package-guides/)
