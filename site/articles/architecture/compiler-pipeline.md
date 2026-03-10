---
title: "Compiler Pipeline"
---

# Compiler Pipeline

This page describes the end-to-end path from a XAML file in a project to generated C# and runtime registration artifacts.

## Stage 1: input discovery

The compiler host discovers:

- XAML additional files
- linked files and target paths
- configuration documents
- transform rules
- global include/theme relationships

This work is done before framework-specific semantics are applied so the host can build a stable view of the project graph.

## Stage 2: parsing and feature enrichment

Documents are parsed into a framework-agnostic structure and then enriched with higher-level features such as binding, selector, property-element, include, and markup semantics.

## Stage 3: framework binding

The selected framework profile applies framework-specific meaning. For Avalonia that includes:

- compiled binding semantics
- expression/event binding lowering
- control themes and selectors
- resource/include handling
- emitted object-graph construction patterns

## Stage 4: generation and registration

The emitter produces generated C# and the related runtime registration data used by hot reload, hot design, and runtime fallback services.

## Stage 5: downstream tooling reuse

The same semantic models are also reused by the language service, which is why editor navigation and diagnostics can stay aligned with compiler behavior.

## Key assemblies

- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.Generator`

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model.md)
- [Binding and Expression Model](../concepts/binding-and-expression-model.md)
- [Generated Artifacts and Runtime Contracts](../concepts/generated-artifacts-and-runtime.md)
