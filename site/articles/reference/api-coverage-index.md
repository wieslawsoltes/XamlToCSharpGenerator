---
title: "API Coverage Index"
---

# API Coverage Index

This index maps the shipped AXSG package surface to the narrative docs and generated API reference.

## Package groups

| Group | Packages | Primary docs |
| --- | --- | --- |
| Compiler host and contracts | `XamlToCSharpGenerator.Compiler`, `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.Framework.Abstractions` | [concepts/compiler-host-and-project-model.md](../concepts/compiler-host-and-project-model.md), [reference/namespace-compiler-and-core.md](namespace-compiler-and-core.md) |
| Framework profiles and parsers | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.NoUi`, `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.MiniLanguageParsing`, `XamlToCSharpGenerator.Generator` | [concepts/binding-and-expression-model.md](../concepts/binding-and-expression-model.md), [reference/namespace-expression-and-framework.md](namespace-expression-and-framework.md) |
| Runtime and editor | `XamlToCSharpGenerator.Runtime`, `XamlToCSharpGenerator.Runtime.Core`, `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.Editor.Avalonia` | [concepts/generated-artifacts-and-runtime.md](../concepts/generated-artifacts-and-runtime.md), [reference/namespace-runtime-and-editor.md](namespace-runtime-and-editor.md) |
| Tooling and integration | `XamlToCSharpGenerator.LanguageService`, `XamlToCSharpGenerator.LanguageServer.Tool`, `XamlToCSharpGenerator.Build`, `XamlToCSharpGenerator` | [concepts/tooling-surface.md](../concepts/tooling-surface.md), [reference/namespace-language-service-and-tooling.md](namespace-language-service-and-tooling.md) |

## Package guide coverage

Every shipped package and editor/tool surface now has a dedicated narrative page:

- [Package Guides](packages/readme.md)
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

- `XamlToCSharpGenerator`
- `XamlToCSharpGenerator.Build`
- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Framework.Abstractions`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.ExpressionSemantics`
- `XamlToCSharpGenerator.MiniLanguageParsing`
- `XamlToCSharpGenerator.NoUi`
- `XamlToCSharpGenerator.Generator`
- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`
- `XamlToCSharpGenerator.LanguageService`
- `XamlToCSharpGenerator.LanguageServer.Tool`
- `XamlToCSharpGenerator.Editor.Avalonia`

Non-packable/internal support projects are documented narratively when generated API would add little value or no stable namespace surface. The main example is:

- `XamlToCSharpGenerator.DotNetWatch.Proxy` via [Internal Support Components](internal-support-components.md)

## Namespace entry points

- [Compiler and Core Namespaces](namespace-compiler-and-core.md)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework.md)
- [Runtime and Editor Namespaces](namespace-runtime-and-editor.md)
- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling.md)

## Notes

- Generated API pages remain the member-level authority.
- Package guides and namespace pages provide the orientation missing from the raw generated API tree.
