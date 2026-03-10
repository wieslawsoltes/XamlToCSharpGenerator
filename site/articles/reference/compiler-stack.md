---
title: "Compiler Stack Packages"
---

# Compiler Stack Packages

This guide explains how the compiler-facing packages fit together, which layer owns what, and how to choose the right entry point when you are extending or embedding AXSG.

## Stack layout

| Layer | Packages | Purpose |
| --- | --- | --- |
| Host and orchestration | `XamlToCSharpGenerator.Compiler` | project discovery, configuration precedence, include graphs, transform-rule application |
| Shared contracts and models | `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.Framework.Abstractions` | semantic contracts, diagnostics, parser helpers, framework profile seams |
| Framework implementation | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.NoUi` | binder/emitter/profile implementations |
| Expression and parsing support | `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.MiniLanguageParsing` | Roslyn-backed C# analysis and low-allocation mini-language parsers |
| Roslyn generator bridge | `XamlToCSharpGenerator.Generator` | analyzer/source-generator entry point that drives the host from MSBuild/Roslyn |

## Recommended entry points

### Application or library build integration

Use:

- [Package: XamlToCSharpGenerator](xamltocsharpgenerator/)
- [Package: XamlToCSharpGenerator.Build](build/)

Do not start from `Compiler` or `Generator` unless you are deliberately composing the lower-level pieces yourself.

### Custom tooling or embedding

Use:

- [Package: XamlToCSharpGenerator.Compiler](compiler/)
- [Package: XamlToCSharpGenerator.Core](core/)
- [Package: XamlToCSharpGenerator.Framework.Abstractions](framework-abstractions/)

Add a concrete profile package such as:

- [Package: XamlToCSharpGenerator.Avalonia](avalonia/)
- [Package: XamlToCSharpGenerator.NoUi](noui/)

### Custom framework profile work

Start from:

- [Package: XamlToCSharpGenerator.Framework.Abstractions](framework-abstractions/)
- [Package: XamlToCSharpGenerator.NoUi](noui/)
- [Custom Framework Profiles](../advanced/custom-framework-profiles/)

`NoUi` is the leanest end-to-end reference profile in the repo and is the right place to learn the profile seams before touching the Avalonia implementation.

## Key concepts by layer

### `Compiler`

Owns project-level normalization:

- additional-file discovery
- configuration source precedence
- transform-rule merging
- include URI resolution
- document convention inference
- cross-document diagnostics such as include cycles

Primary API:

- <xref:XamlToCSharpGenerator.Compiler.XamlSourceGeneratorCompilerHost>

### `Core`

Owns the cross-layer semantic contracts:

- document models
- binding/resource/style models
- parser semantics
- diagnostics
- configuration models

Primary APIs:

- <xref:XamlToCSharpGenerator.Core.Models.ResolvedCompiledBindingDefinition>
- <xref:XamlToCSharpGenerator.Core.Diagnostics.DiagnosticCatalog>
- <xref:XamlToCSharpGenerator.Core.Configuration.XamlSourceGenConfiguration>

### `Framework.Abstractions`

Owns framework seams:

- framework profile interfaces
- binder/emitter contracts
- framework-level service points

Primary APIs:

- <xref:XamlToCSharpGenerator.Framework.Abstractions.IXamlFrameworkProfile>
- <xref:XamlToCSharpGenerator.Core.Abstractions.IXamlSemanticBinder>
- <xref:XamlToCSharpGenerator.Core.Abstractions.IXamlCodeEmitter>

### `Avalonia` and `NoUi`

Own the framework-specific lowering and emission rules:

- binding semantics
- style/template/control-theme semantics
- generated helper emission
- framework profile creation

Primary APIs:

- <xref:XamlToCSharpGenerator.Avalonia.Binding.AvaloniaSemanticBinder>
- <xref:XamlToCSharpGenerator.Avalonia.Emission.AvaloniaCodeEmitter>
- <xref:XamlToCSharpGenerator.Avalonia.Framework.AvaloniaFrameworkProfile>

### `ExpressionSemantics` and `MiniLanguageParsing`

Own the hot-path analysis/parsing layers:

- C# expression and lambda analysis
- shorthand expression interpretation
- selector parsing
- binding/event/query parsing

Primary APIs:

- <xref:XamlToCSharpGenerator.ExpressionSemantics.CSharpMarkupExpressionSemantics>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Selectors.SelectorReferenceSemantics>

## Related docs

- [Compiler and Core Namespaces](namespace-compiler-and-core/)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
- [Compiler Pipeline](../architecture/compiler-pipeline/)
