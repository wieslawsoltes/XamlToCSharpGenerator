---
title: "Package: XamlToCSharpGenerator.NoUi"
---

# XamlToCSharpGenerator.NoUi

## Role

A framework-neutral pilot profile used to validate compiler-host reuse outside Avalonia.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.NoUi" Version="0.1.0-alpha.3" />
```

## Related namespaces

- <xref:XamlToCSharpGenerator.NoUi>
- <xref:XamlToCSharpGenerator.NoUi.Binding>
- <xref:XamlToCSharpGenerator.NoUi.Emission>
- <xref:XamlToCSharpGenerator.NoUi.Framework>

## Use it when

- you are experimenting with framework-agnostic host reuse
- you want a minimal profile reference implementation

## What lives here

`XamlToCSharpGenerator.NoUi` is the smallest end-to-end profile in the repo. It is useful when you want to understand:

- how the compiler host interacts with a framework profile
- which services are truly framework-specific
- how much of AXSG can be reused outside Avalonia

It is also the best starting point when designing a new profile from scratch.

## Why it matters even if you never ship it

`NoUi` is useful as an architectural reference because it makes the host/profile boundary obvious. It shows which services are truly framework-specific and which ones belong in the shared compiler stack.

## Related docs

- [Custom Framework Profiles](../advanced/custom-framework-profiles/)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
