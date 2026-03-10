---
title: "Package: XamlToCSharpGenerator.Core"
---

# XamlToCSharpGenerator.Core

## Role

Core abstractions, diagnostics, models, and XAML token semantics shared by every compiler/runtime/tooling layer.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.Core" Version="0.1.0-alpha.3" />
```

## Related namespaces

- <xref:XamlToCSharpGenerator.Core.Abstractions>
- <xref:XamlToCSharpGenerator.Core.Configuration>
- <xref:XamlToCSharpGenerator.Core.Diagnostics>
- <xref:XamlToCSharpGenerator.Core.Models>
- <xref:XamlToCSharpGenerator.Core.Parsing>

## Use it when

- you need the semantic contracts used by profiles and tools
- you are implementing parser, binder, or emitter extensions
- you need stable diagnostics/models in tests or custom tooling

## What lives here

`XamlToCSharpGenerator.Core` contains the contracts everything else builds on:

- document/configuration models
- diagnostics and diagnostic descriptors
- parser/token semantic helpers
- binding, event, selector, resource, and property-element model types
- abstraction interfaces shared between compiler, runtime, and language-service layers

If a change belongs to the stable semantic model rather than to one framework profile or one editor host, it usually belongs here.

## Typical use cases

Reach for `Core` when you need:

- the canonical diagnostics and model types
- parser/token semantics without the full framework layer
- shared abstractions that must stay neutral across compiler, runtime, and tooling

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
- [Compiler and Core Namespaces](../namespace-compiler-and-core/)
- [Configuration Model](../configuration-model/)
