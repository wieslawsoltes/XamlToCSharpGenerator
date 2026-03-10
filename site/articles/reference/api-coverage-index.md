---
title: "API Coverage Index"
---

# API Coverage Index

This index maps the shipped AXSG package surface to the narrative docs and generated API reference.

## Package groups

| Group | Packages | Primary docs |
| --- | --- | --- |
| Compiler host and contracts | `XamlToCSharpGenerator.Compiler`, `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.Framework.Abstractions` | [Compiler Host and Project Model](../concepts/compiler-host-and-project-model.md), [Compiler and Core Namespaces](namespace-compiler-and-core.md) |
| Framework profiles and parsers | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.NoUi`, `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.MiniLanguageParsing`, `XamlToCSharpGenerator.Generator` | [Binding and Expression Model](../concepts/binding-and-expression-model.md), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework.md) |
| Runtime and editor | `XamlToCSharpGenerator.Runtime`, `XamlToCSharpGenerator.Runtime.Core`, `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.Editor.Avalonia` | [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime.md), [Runtime and Editor Namespaces](namespace-runtime-and-editor.md) |
| Tooling and integration | `XamlToCSharpGenerator.LanguageService`, `XamlToCSharpGenerator.LanguageServer.Tool`, `XamlToCSharpGenerator.Build`, `XamlToCSharpGenerator` | [Tooling Surface](../concepts/tooling-surface.md), [Language Service and Tooling Namespaces](namespace-language-service-and-tooling.md) |

## Package and assembly mapping

Use [Package and Assembly](package-and-assembly.md) when you need the installation identity, shipped payload, and generated API status for each artifact.
Use [Assembly Catalog](assembly-catalog.md) when you already know the assembly or shipped artifact name and want the direct generated API route plus the matching narrative guide.
Use [Feature Coverage Matrix](feature-coverage-matrix.md) when you know the feature area but need to map it to the right package and namespace entry point.

## Package guide coverage

Every shipped package and editor/tool surface now has a dedicated narrative page:

- [Package Guides](packages/)
- [XamlToCSharpGenerator](packages/xamltocsharpgenerator.md)
- [XamlToCSharpGenerator.Build](packages/build.md)
- [XamlToCSharpGenerator.Compiler](packages/compiler.md)
- [XamlToCSharpGenerator.Core](packages/core.md)
- [XamlToCSharpGenerator.Framework.Abstractions](packages/framework-abstractions.md)
- [XamlToCSharpGenerator.Avalonia](packages/avalonia.md)
- [XamlToCSharpGenerator.ExpressionSemantics](packages/expression-semantics.md)
- [XamlToCSharpGenerator.MiniLanguageParsing](packages/mini-language-parsing.md)
- [XamlToCSharpGenerator.NoUi](packages/noui.md)
- [XamlToCSharpGenerator.Generator](packages/generator.md)
- [XamlToCSharpGenerator.Runtime](packages/runtime.md)
- [XamlToCSharpGenerator.Runtime.Core](packages/runtime-core.md)
- [XamlToCSharpGenerator.Runtime.Avalonia](packages/runtime-avalonia.md)
- [XamlToCSharpGenerator.LanguageService](packages/language-service.md)
- [XamlToCSharpGenerator.LanguageServer.Tool](packages/language-server-tool.md)
- [XamlToCSharpGenerator.Editor.Avalonia](packages/editor-avalonia.md)
- [VS Code Extension](packages/vscode-extension.md)

## Generated API scope

The Lunet `api.dotnet` build on this branch covers these projects:

- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Framework.Abstractions`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.ExpressionSemantics`
- `XamlToCSharpGenerator.MiniLanguageParsing`
- `XamlToCSharpGenerator.NoUi`
- `XamlToCSharpGenerator.Generator`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`
- `XamlToCSharpGenerator.LanguageService`
- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.Editor.Avalonia`

For a direct assembly-to-API route map, use [Assembly Catalog](assembly-catalog.md).

Narrative-only shipped/internal artifacts are documented outside generated API when a package is primarily a package shell, MSBuild targets layer, or operational executable. The main examples are:

- `XamlToCSharpGenerator` via [Package Guides](packages/) and [Package and Assembly](package-and-assembly.md)
- `XamlToCSharpGenerator.Build` via [Package Guides](packages/) and [Package and Assembly](package-and-assembly.md)
- `XamlToCSharpGenerator.Runtime` via [Package Guides](packages/) and [Package and Assembly](package-and-assembly.md)
- `XamlToCSharpGenerator.DotNetWatch.Proxy` via [Internal Support Components](internal-support-components.md)

These are intentionally excluded from generated API because they do not provide a stable, useful public namespace surface on their own.

## Namespace entry points

- [Compiler and Core Namespaces](namespace-compiler-and-core.md)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework.md)
- [Runtime and Editor Namespaces](namespace-runtime-and-editor.md)
- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling.md)

## Notes

- Generated API pages remain the member-level authority.
- Package guides and namespace pages provide the orientation missing from the raw generated API tree.
