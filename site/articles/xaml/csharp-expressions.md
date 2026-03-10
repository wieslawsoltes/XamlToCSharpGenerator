---
title: "C# Expressions"
---

# C# Expressions

AXSG supports C# expression syntax inside valid XAML forms. This includes simple shorthand expressions, full explicit expressions, interpolation, formatting, ternaries, boolean logic, and source-aware lowering when the expression can be converted into a more efficient binding form.

## Supported forms

The expression surface includes:

- shorthand expressions such as `{Title}` or `{HasAccount && AgreedToTerms}`
- explicit expression forms where the compiler can clearly identify a C# expression body
- interpolated strings and formatted numeric expressions
- source-qualified forms such as root- or data-context-directed access where supported by the current semantics

## Why this matters

These expressions are not handled as plain text macros. AXSG parses and analyzes them against the current XAML scope so it can:

- resolve the correct source object
- validate members and methods at build time
- generate better runtime helpers
- surface language-service completion, hover, references, and definitions

## Interactions with compiled bindings

In many cases AXSG can lower a shorthand expression to a compiled binding path or another cheaper generated form. In other cases it keeps the expression as an analyzed C# expression with generated runtime support. The compiler chooses the lowering based on precedence and source resolution rules.

## Where to look next

- [Guides: C# Expressions](../guides/csharp-expressions.md)
- [Inline C#](inline-csharp.md) for statement-capable code forms
- [Binding and Expression Model](../concepts/binding-and-expression-model.md)
- APIs: <xref:XamlToCSharpGenerator.ExpressionSemantics>, <xref:XamlToCSharpGenerator.Avalonia.Binding>
