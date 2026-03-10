---
title: "Expression, Parsing, and Framework Namespaces"
---

# Expression, Parsing, and Framework Namespaces

This namespace family covers expression analysis, low-allocation mini-language parsing, and the Avalonia/NoUi framework profiles that turn generic compiler contracts into real generated output.

## Packages behind this area

- `XamlToCSharpGenerator.ExpressionSemantics`
- `XamlToCSharpGenerator.MiniLanguageParsing`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.NoUi`
- `XamlToCSharpGenerator.Generator`

## Primary namespaces

- <xref:XamlToCSharpGenerator.ExpressionSemantics>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Bindings>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Selectors>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Text>
- <xref:XamlToCSharpGenerator.Avalonia.Binding>
- <xref:XamlToCSharpGenerator.Avalonia.Emission>
- <xref:XamlToCSharpGenerator.Avalonia.Framework>
- <xref:XamlToCSharpGenerator.Avalonia.Parsing>
- <xref:XamlToCSharpGenerator.NoUi>
- <xref:XamlToCSharpGenerator.Generator>

## What lives here

### Expression and inline-code analysis

`ExpressionSemantics` owns Roslyn-backed analysis for:

- shorthand expressions
- interpolated and formatted expressions
- inline `CSharp` code and CDATA blocks
- inline event lambdas and statement blocks

### Mini-language parsing

`MiniLanguageParsing` handles selectors, binding fragments, event-binding fragments, and other hot-path text grammars where the full XAML parser would be too expensive.

### Framework profiles and emission

`Avalonia.*` namespaces define the real binding rules, control-theme/style semantics, and code emission for Avalonia. `NoUi` is the smallest reference profile for framework-neutral experiments.

### Generator entry point

`XamlToCSharpGenerator.Generator` is the Roslyn-facing analyzer/source-generator wrapper that feeds the compiler host with project inputs and publishes generated source/diagnostics.

## Use this area when

- you are changing expression semantics or shorthand lowering
- you are debugging selectors, resource markup, or binding fragment parsing
- you need to understand how Avalonia-specific rules differ from the generic host
- you are investigating generated helper code or profile-specific lowering

## Suggested API entry points

- <xref:XamlToCSharpGenerator.ExpressionSemantics.CSharpMarkupExpressionSemantics>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Selectors.SelectorReferenceSemantics>
- <xref:XamlToCSharpGenerator.Avalonia.Binding.AvaloniaSemanticBinder>
- <xref:XamlToCSharpGenerator.Avalonia.Emission.AvaloniaCodeEmitter>

## Related docs

- [Binding and Expression Model](../concepts/binding-and-expression-model)
- [C# Expressions](../xaml/csharp-expressions)
- [Inline C#](../xaml/inline-csharp)
- [Styles, Templates, and Themes](../xaml/styles-templates-and-themes)
