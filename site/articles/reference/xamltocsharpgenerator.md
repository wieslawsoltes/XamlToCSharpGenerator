---
title: "Package: XamlToCSharpGenerator"
---

# XamlToCSharpGenerator

## Role

The umbrella package for application authors. It brings in the standard build, compiler, and runtime pieces for source-generated Avalonia XAML.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
```

## Use it when

- you want the normal app-facing install surface
- you do not need to compose individual compiler/runtime packages manually
- you want generated XAML plus the default runtime bootstrap path

## What it brings in

The umbrella package is the recommended starting point for applications because it bundles the normal pieces together:

- build imports from `XamlToCSharpGenerator.Build`
- analyzer/generator payloads for compilation
- runtime libraries for generated output execution
- the standard project-level integration path used by the sample applications

Use individual packages only when you have a concrete reason to split the surface.

## Why this is the default recommendation

Most application authors do not want to compose build imports, generator payloads, and runtime dependencies manually. The umbrella package exists so a normal app can adopt AXSG with one reference and move directly to authored XAML, diagnostics, and runtime validation.

## What it does not replace

This package does not make the lower-level artifacts irrelevant. You still go to the lower-level packages when you need:

- explicit build customization
- standalone language-server hosting
- embedded editor surfaces
- framework-agnostic profile work

It also does not remove the need for the lower-level docs. Once you move beyond the default install path, the package guides and namespace summaries remain the authoritative way to locate the real implementation layer for a feature.

## Related namespaces

- <xref:XamlToCSharpGenerator.Runtime>
- <xref:XamlToCSharpGenerator.Runtime.Markup>

## Related docs

- [Why AXSG](../getting-started/overview/)
- [Package Selection and Integration](../guides/package-selection-and-integration/)
- [Artifact Matrix](artifact-matrix/)
- [Package and Assembly](package-and-assembly/)
- [Package Guides](package-guides/)
