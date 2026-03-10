---
title: "API Navigation Guide"
---

# API Navigation Guide

The generated API is large. This page tells you how to enter it without guessing.

## Start from the right reference page

Use:

- [Package Catalog](package-catalog/) when you know the package name
- [Package and Assembly](package-and-assembly/) when you need the install-to-assembly mapping
- [Assembly Catalog](assembly-catalog/) when you already know the assembly
- [Feature Coverage Matrix](feature-coverage-matrix/) when you know the feature area but not the package

## Namespace entry strategy

The generated API tree is easiest to navigate by subsystem:

- [Compiler and Core Namespaces](namespace-compiler-and-core/)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/)
- [Runtime and Editor Namespaces](namespace-runtime-and-editor/)
- [Language Service and Tooling Namespaces](namespace-language-service-and-tooling/)

These narrative pages exist because the raw generated API tree does not explain responsibility boundaries.

## Typical API entry points

| You need to inspect | Start here |
| --- | --- |
| compiler host configuration and input normalization | `/api/XamlToCSharpGenerator.Compiler/index.html` |
| parser contracts and semantic models | `/api/XamlToCSharpGenerator.Core/index.html` |
| Avalonia binding/emission pipeline | `/api/XamlToCSharpGenerator.Avalonia.Binding/index.html` |
| expression analysis and inline C# | `/api/XamlToCSharpGenerator.ExpressionSemantics/index.html` |
| runtime helpers and markup support | `/api/XamlToCSharpGenerator.Runtime.Avalonia/index.html` |
| completion/definition/reference pipeline | `/api/XamlToCSharpGenerator.LanguageService/index.html` |
| standalone server hosting | `/api/XamlToCSharpGenerator.LanguageServer/index.html` |
| embedded editor control | `/api/XamlToCSharpGenerator.Editor.Avalonia/index.html` |

## What is intentionally narrative-only

Do not look for raw generated API first for these artifacts:

- `XamlToCSharpGenerator`
- `XamlToCSharpGenerator.Build`
- `XamlToCSharpGenerator.Runtime`
- VS Code extension package

Those are documented through package/reference guides because their public value is composition, packaging, or host integration rather than a deep namespace surface.

## External xref gaps

Some generated pages reference external types such as `AvaloniaEdit.*`. If Lunet cannot resolve those external xrefs, the AXSG page still builds, but the external links remain plain text.

That is an external API-doc coverage issue, not a missing AXSG API page.
