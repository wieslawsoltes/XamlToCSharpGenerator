---
title: "XAML Features"
---

# XAML Features

These pages document the language-level surface AXSG adds or strengthens on top of standard Avalonia XAML. Use this section when you care about authoring semantics rather than package boundaries.

## Feature groups

### Binding and expression features

- [Compiled Bindings](compiled-bindings.md)
- [C# Expressions](csharp-expressions.md)
- [Inline C#](inline-csharp.md)
- [Event Bindings](event-bindings.md)

### Structural and configuration features

- [Conditional XAML](conditional-xaml.md)
- [Resources, Includes, and URIs](resources-includes-and-uris.md)
- [Property Elements, TemplateBinding, and Attached Properties](property-elements-templatebinding-and-attached-properties.md)
- [Global XML Namespaces and Project Configuration](global-xmlns-and-project-configuration.md)
- [Styles, Templates, and Themes](styles-templates-and-themes.md)

## How to use this section

- Start with [Compiled Bindings](compiled-bindings.md) if you are new to AXSG semantics.
- Use [C# Expressions](csharp-expressions.md) and [Inline C#](inline-csharp.md) for the main language extensions.
- Use the structural pages when you are working in themes, resources, includes, or selector-heavy XAML.
- Use [Guides](../guides/) when you need an operational workflow instead of language semantics.

## Typical questions this section answers

- when does AXSG require `x:DataType` and when can an explicit source provide type context?
- how do shorthand expressions, inline C#, and event lambdas differ?
- how do property elements, `TemplateBinding`, resources, and URI values participate in navigation and diagnostics?
- how do styles, themes, selectors, and control-theme override patterns behave under compilation?

## Related docs

- [Binding and Expression Model](../concepts/binding-and-expression-model.md)
- [Feature Coverage Matrix](../reference/feature-coverage-matrix.md)
- [Samples and Feature Tour](../getting-started/samples-and-feature-tour.md)
