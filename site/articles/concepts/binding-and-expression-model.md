---
title: "Binding and Expression Model"
---

# Binding and Expression Model

AXSG supports multiple binding surfaces that all flow through the semantic binder.

## Binding kinds

The binder resolves and emits:

- classic bindings
- compiled bindings
- selector/property mini-languages
- C# expressions and shorthand expressions
- inline C# blocks and event lambdas

Key shared packages:

- <xref:XamlToCSharpGenerator.ExpressionSemantics>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Bindings>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Selectors>
- <xref:XamlToCSharpGenerator.Avalonia.Binding>

## Resolution model

Resolution happens in layers:

1. parse XAML markup and mini-language fragments
2. resolve XML namespaces, CLR types, and properties
3. determine source context (`x:DataType`, relative source, template scope, inline event scope)
4. lower the construct to generated C# and runtime helpers when needed

## Runtime contract

Some forms compile to direct CLR/property access. Others compile to helper contracts in:

- <xref:XamlToCSharpGenerator.Runtime>
