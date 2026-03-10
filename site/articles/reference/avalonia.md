---
title: "Package: XamlToCSharpGenerator.Avalonia"
---

# XamlToCSharpGenerator.Avalonia

## Role

Avalonia-specific binder, emitter, parsing enrichment, and framework-profile implementation.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.Avalonia" Version="0.1.0-alpha.3" />
```

## Related namespaces

- <xref:XamlToCSharpGenerator.Avalonia.Binding>
- <xref:XamlToCSharpGenerator.Avalonia.Emission>
- <xref:XamlToCSharpGenerator.Avalonia.Framework>
- <xref:XamlToCSharpGenerator.Avalonia.Parsing>

## Use it when

- you want the Avalonia compiler profile directly
- you are extending Avalonia-specific compile-time behavior

## What lives here

This package owns the Avalonia-specific translation layer:

- binding lowering for compiled bindings, shorthand expressions, event bindings, and inline code
- control-theme, template, style, and resource semantics
- code emission for generated object graphs, helper methods, and runtime descriptors
- Avalonia-specific feature enrichment over the generic compiler host

This is where most user-visible language features become concrete Avalonia behavior. If authored XAML is valid in principle but lowers or emits incorrectly for Avalonia, this is usually the first package to inspect.

## Typical companions

- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.ExpressionSemantics`
- `XamlToCSharpGenerator.Framework.Abstractions`
- `XamlToCSharpGenerator.MiniLanguageParsing`

## Common change categories

Most work in this package falls into:

- binding and expression lowering
- selector, template, theme, and resource semantics
- generated helper and object-graph emission
- framework-specific diagnostics and runtime descriptors

## Related docs

- [Binding and Expression Model](../concepts/binding-and-expression-model/)
- [Styles, Templates, and Themes](../../xaml/styles-templates-and-themes/)
- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules/)
- [Expression, Parsing, and Framework Namespaces](../namespace-expression-and-framework/)
