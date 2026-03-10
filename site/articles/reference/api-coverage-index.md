---
title: "API Coverage Index"
---

# API Coverage Index

This index maps the shipped AXSG package surface to the narrative docs and generated API reference.

## Package groups

| Group | Packages | Primary docs |
| --- | --- | --- |
| Compiler host and contracts | `XamlToCSharpGenerator.Compiler`, `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.Framework.Abstractions` | [concepts/compiler-host-and-project-model.md](../concepts/compiler-host-and-project-model), [reference/namespace-compiler-and-core.md](namespace-compiler-and-core) |
| Framework profiles and parsers | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.NoUi`, `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.MiniLanguageParsing`, `XamlToCSharpGenerator.Generator` | [concepts/binding-and-expression-model.md](../concepts/binding-and-expression-model), [reference/namespace-expression-and-framework.md](namespace-expression-and-framework) |
| Runtime and editor | `XamlToCSharpGenerator.Runtime`, `XamlToCSharpGenerator.Runtime.Core`, `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.Editor.Avalonia` | [concepts/generated-artifacts-and-runtime.md](../concepts/generated-artifacts-and-runtime), [reference/namespace-runtime-and-editor.md](namespace-runtime-and-editor) |
| Tooling and integration | `XamlToCSharpGenerator.LanguageService`, `XamlToCSharpGenerator.LanguageServer.Tool`, `XamlToCSharpGenerator.Build`, `XamlToCSharpGenerator` | [concepts/tooling-surface.md](../concepts/tooling-surface), [reference/namespace-language-service-and-tooling.md](namespace-language-service-and-tooling) |

## Package guide coverage

Every shipped package and editor/tool surface now has a dedicated narrative page:

- [Package Guides](packages/readme)
- [XamlToCSharpGenerator](packages/xamltocsharpgenerator)
- [XamlToCSharpGenerator.Build](packages/build)
- [XamlToCSharpGenerator.Compiler](packages/compiler)
- [XamlToCSharpGenerator.Core](packages/core)
- [XamlToCSharpGenerator.Framework.Abstractions](packages/framework-abstractions)
- [XamlToCSharpGenerator.Avalonia](packages/avalonia)
- [XamlToCSharpGenerator.ExpressionSemantics](packages/expression-semantics)
- [XamlToCSharpGenerator.MiniLanguageParsing](packages/mini-language-parsing)
- [XamlToCSharpGenerator.NoUi](packages/noui)
- [XamlToCSharpGenerator.Generator](packages/generator)
- [XamlToCSharpGenerator.Runtime](packages/runtime)
- [XamlToCSharpGenerator.Runtime.Core](packages/runtime-core)
- [XamlToCSharpGenerator.Runtime.Avalonia](packages/runtime-avalonia)
- [XamlToCSharpGenerator.LanguageService](packages/language-service)
- [XamlToCSharpGenerator.LanguageServer.Tool](packages/language-server-tool)
- [XamlToCSharpGenerator.Editor.Avalonia](packages/editor-avalonia)
- [VS Code Extension](packages/vscode-extension)

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

- `XamlToCSharpGenerator.DotNetWatch.Proxy` via [Internal Support Components](internal-support-components)

## Namespace entry points

- [Compiler and Core Namespaces](namespace-compiler-and-core)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework)
- [Runtime and Editor Namespaces](namespace-runtime-and-editor)
- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling)

## Notes

- Generated API pages remain the member-level authority.
- Package guides and namespace pages provide the orientation missing from the raw generated API tree.
