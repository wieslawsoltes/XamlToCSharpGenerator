---
title: "Package: XamlToCSharpGenerator.Compiler"
---

# XamlToCSharpGenerator.Compiler

## Role

Framework-agnostic compiler host orchestration. This package owns document discovery, configuration precedence, transform application, include graphs, and generator input normalization.

## Related namespaces

- <xref:XamlToCSharpGenerator.Compiler>

## Use it when

- you are embedding the compiler host in another tool
- you want to compose a custom framework profile
- you need project-model behavior without taking the full app runtime

## Core responsibilities

The compiler host owns:

- additional-file discovery and normalization
- include-graph analysis
- configuration precedence and transform-rule merging
- document convention inference
- invocation of the selected framework binder and emitter

It also owns several determinism-sensitive behaviors that show up later in incremental builds, hot reload, and regression testing:

- stable document identity
- include graph normalization
- configuration precedence
- transform convergence
- deterministic input ordering

## Common companions

`XamlToCSharpGenerator.Compiler` is usually paired with:

- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Framework.Abstractions`
- a concrete profile such as `XamlToCSharpGenerator.Avalonia` or `XamlToCSharpGenerator.NoUi`

## Typical extension work

You work in this package when you are changing project discovery, configuration loading, transform-rule handling, include graphs, or the way the host prepares inputs for a framework profile. If the issue is purely Avalonia-specific after those inputs are already correct, it likely belongs in `XamlToCSharpGenerator.Avalonia`.

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
- [Configuration Model](configuration-model/)
- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules/)
- [Compiler and Core Namespaces](namespace-compiler-and-core/)
