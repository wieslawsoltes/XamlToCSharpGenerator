---
title: "Compiled Bindings"
---

# Compiled Bindings

Compiled bindings are one of the main reasons to adopt AXSG. Instead of treating a binding path as opaque text, AXSG resolves the source context, validates member access, and emits generated code or generated runtime descriptors based on that semantic model.

## What AXSG validates

Compiled binding analysis can validate:

- source-context availability through `x:DataType` or explicit typed sources
- property and method names used by binding paths and expressions
- relative source and parent/self binding shapes
- selector, setter, and control-theme binding usage in non-trivial scopes

## Where compiled bindings apply

You should think about compiled bindings as a language feature, not only a `Text="{Binding Foo}"` feature. AXSG uses the same semantic machinery for:

- classic binding markup where compiled semantics are enabled
- shorthand expression lowering
- control themes and style setters
- template and resource scopes with explicit type context

## When `x:DataType` is required

Ambient `x:DataType` is still the normal way to establish source context. However, AXSG also supports cases where the source type is known through explicit source expressions such as typed parent/self forms. That distinction matters when you are reading diagnostics: missing ambient data type and unknown explicit source are different problems.

## Editor support

The language service reuses the compiler binding model for:

- completion on binding paths
- hover/inlay hints for result types
- definition and reference navigation to bound CLR symbols
- rename/refactoring propagation between C# and XAML

## Related docs

- [C# Expressions](csharp-expressions/)
- [Binding and Expression Model](../concepts/binding-and-expression-model/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
- API: <xref:XamlToCSharpGenerator.Avalonia.Binding>
