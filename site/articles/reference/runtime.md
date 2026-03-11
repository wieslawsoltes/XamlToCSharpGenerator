---
title: "Package: XamlToCSharpGenerator.Runtime"
---

# XamlToCSharpGenerator.Runtime

## Role

Compatibility runtime umbrella that composes `Runtime.Core` and `Runtime.Avalonia` for normal app installs.

## Use it when

- you want runtime support without choosing sublayers manually
- you need hot reload/runtime registries plus Avalonia-specific runtime integration together

## Package shape

`XamlToCSharpGenerator.Runtime` is primarily a composition package. It is the normal runtime-facing install surface for app authors, bringing together:

- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`

Use the subpackages directly when you are embedding or testing specific runtime layers.

## What this package is for

This is the runtime counterpart to the umbrella build/compiler story. It gives application authors a single runtime-facing package that matches the default generated output shape without forcing them to compose every runtime layer manually.

## What it does not contain

This package is mostly composition. If you need member-level runtime internals, go directly to:

- [XamlToCSharpGenerator.Runtime.Core](runtime-core/)
- [XamlToCSharpGenerator.Runtime.Avalonia](runtime-avalonia/)

## Typical escalation path

Start here when you are choosing the default runtime surface.
Move down to the runtime subpackages when you are debugging:

- hot reload state transfer
- runtime loader fallback
- resource/include resolution
- source-info and URI mapping

## Related docs

- [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)
- [Runtime and Editor Namespaces](namespace-runtime-and-editor/)
- [Package Selection and Integration](../guides/package-selection-and-integration/)
