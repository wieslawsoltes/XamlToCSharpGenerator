---
title: "API Coverage Index"
---

# API Coverage Index

This index maps the shipped AXSG package surface to the narrative docs and generated API reference.

## Package groups

| Group | Packages | Primary docs |
| --- | --- | --- |
| Compiler host and contracts | `XamlToCSharpGenerator.Compiler`, `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.Framework.Abstractions` | [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/), [Compiler and Core Namespaces](namespace-compiler-and-core/) |
| Framework profiles and parsers | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.NoUi`, `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.MiniLanguageParsing`, `XamlToCSharpGenerator.Generator` | [Binding and Expression Model](../concepts/binding-and-expression-model/), [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/) |
| Runtime and editor | `XamlToCSharpGenerator.Runtime`, `XamlToCSharpGenerator.Runtime.Core`, `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.Editor.Avalonia` | [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/), [Runtime and Editor Namespaces](namespace-runtime-and-editor/) |
| Tooling and integration | `XamlToCSharpGenerator.LanguageService`, `XamlToCSharpGenerator.LanguageServer.Tool`, `XamlToCSharpGenerator.Build`, `XamlToCSharpGenerator` | [Tooling Surface](../concepts/tooling-surface/), [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/) |

## Package and assembly mapping

Use [Package and Assembly](package-and-assembly/) when you need the installation identity, shipped payload, and generated API status for each artifact.
Use [Assembly Catalog](assembly-catalog/) when you already know the assembly or shipped artifact name and want the direct generated API route plus the matching narrative guide.
Use [Feature Coverage Matrix](feature-coverage-matrix/) when you know the feature area but need to map it to the right package and namespace entry point.

## Package guide coverage

Every shipped package and editor/tool surface now has a dedicated narrative page:

- [Package Guides](package-guides/)
- [XamlToCSharpGenerator](xamltocsharpgenerator/)
- [XamlToCSharpGenerator.Build](build/)
- [XamlToCSharpGenerator.Compiler](compiler/)
- [XamlToCSharpGenerator.Core](core/)
- [XamlToCSharpGenerator.Framework.Abstractions](framework-abstractions/)
- [XamlToCSharpGenerator.Avalonia](avalonia/)
- [XamlToCSharpGenerator.ExpressionSemantics](expression-semantics/)
- [XamlToCSharpGenerator.MiniLanguageParsing](mini-language-parsing/)
- [XamlToCSharpGenerator.NoUi](noui/)
- [XamlToCSharpGenerator.Generator](generator/)
- [XamlToCSharpGenerator.Runtime](runtime/)
- [XamlToCSharpGenerator.Runtime.Core](runtime-core/)
- [XamlToCSharpGenerator.Runtime.Avalonia](runtime-avalonia/)
- [XamlToCSharpGenerator.LanguageService](language-service/)
- [XamlToCSharpGenerator.LanguageServer.Tool](language-server-tool/)
- [XamlToCSharpGenerator.Editor.Avalonia](editor-avalonia/)
- [VS Code Extension](vscode-extension/)

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

For a direct assembly-to-API route map, use [Assembly Catalog](assembly-catalog/).

Narrative-only shipped/internal artifacts are documented outside generated API when a package is primarily a package shell, MSBuild targets layer, or operational executable. The main examples are:

- `XamlToCSharpGenerator` via [Package Guides](package-guides/) and [Package and Assembly](package-and-assembly/)
- `XamlToCSharpGenerator.Build` via [Package Guides](package-guides/) and [Package and Assembly](package-and-assembly/)
- `XamlToCSharpGenerator.Runtime` via [Package Guides](package-guides/) and [Package and Assembly](package-and-assembly/)
- `XamlToCSharpGenerator.DotNetWatch.Proxy` via [Internal Support Components](internal-support-components/)

These are intentionally excluded from generated API because they do not provide a stable, useful public namespace surface on their own.

## Namespace entry points

- [Compiler and Core Namespaces](namespace-compiler-and-core/)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/)
- [Runtime and Editor Namespaces](namespace-runtime-and-editor/)
- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/)

## Notes

- Generated API pages remain the member-level authority.
- Package guides and namespace pages provide the orientation missing from the raw generated API tree.
