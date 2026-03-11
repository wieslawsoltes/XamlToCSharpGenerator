---
title: "Package: XamlToCSharpGenerator.Framework.Abstractions"
---

# XamlToCSharpGenerator.Framework.Abstractions

## Role

Contracts for framework-specific profiles: binder/emitter/profile interfaces and integration points used to plug a UI framework into the generic compiler host.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.Framework.Abstractions" Version="<VERSION>" />
```

## Related namespaces

- <xref:XamlToCSharpGenerator.Framework.Abstractions>

## Use it when

- you are building another framework profile on top of AXSG
- you need to swap or test binder/emitter implementations

## What lives here

This package defines the extension seam between the generic compiler host and a concrete UI framework:

- framework profile contracts
- binder/emitter interfaces
- framework-level configuration abstractions
- profile services used by host composition and tests

It is intentionally small but strategically important. This is the boundary that keeps the compiler host open for new framework profiles without leaking Avalonia-specific assumptions into the shared host.

## Typical extension work

Reach for this package when you are:

- defining a new framework profile
- testing alternative binder/emitter implementations
- deciding whether a new feature belongs in the host contract or in a concrete profile

## Related docs

- [Custom Framework Profiles](../advanced/custom-framework-profiles/)
- [Expression, Parsing, and Framework Namespaces](namespace-expression-and-framework/)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
