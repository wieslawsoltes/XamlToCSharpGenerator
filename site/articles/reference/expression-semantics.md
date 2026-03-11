---
title: "Package: XamlToCSharpGenerator.ExpressionSemantics"
---

# XamlToCSharpGenerator.ExpressionSemantics

## Role

Roslyn-based analysis for expressions, lambdas, shorthand forms, inline C#, and source-context symbol tracking.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.ExpressionSemantics" Version="0.1.0-alpha.3" />
```

## Related namespaces

- <xref:XamlToCSharpGenerator.ExpressionSemantics>

## Use it when

- you need reusable expression analysis outside the Avalonia binder
- you are extending shorthand, inline-code, or lambda semantics

## What lives here

This package provides the Roslyn-backed semantic analysis used by:

- shorthand expressions such as `{Name}` and `{this.RootProperty}`
- C# expressions and string interpolation
- inline `CSharp` markup and CDATA bodies
- inline event lambdas and statement bodies
- symbol/range mapping back into the original XAML source

The same analysis is reused by both compiler and tooling flows, which is why shorthand and inline-code features can stay consistent across build-time diagnostics and editor-time navigation.

## Typical use cases

Use this package directly when you need:

- reusable expression analysis outside the full Avalonia profile
- source-context symbol mapping for tooling or tests
- standalone work on shorthand, interpolation, or inline-code semantics

## Related docs

- [C# Expressions](../xaml/csharp-expressions/)
- [Inline C#](../xaml/inline-csharp/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
