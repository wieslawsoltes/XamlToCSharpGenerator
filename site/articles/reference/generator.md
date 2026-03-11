---
title: "Package: XamlToCSharpGenerator.Generator"
---

# XamlToCSharpGenerator.Generator

## Role

The standalone Roslyn source generator backend. This is the analyzer/generator assembly consumed by builds.

## Install

Most users should not reference this package directly. Prefer the umbrella package or build package.

## Use it when

- you need the generator assembly directly
- you are embedding or testing generator behavior without the umbrella package

## What lives here

This package contains the source-generator entry point and the Roslyn-facing glue that connects:

- additional files from the project
- compiler host setup
- framework profile selection
- generated source emission
- diagnostics flowing back into the IDE/build

It is primarily useful for advanced tooling or generator test harnesses.

Most consumers should not reference this package directly. It exists so the generator payload can be versioned, tested, and integrated independently when needed.

## Typical scenarios

- Roslyn generator test harnesses
- analyzer packaging/debugging
- advanced build integration where generator payload selection must be explicit

## Related namespaces

- <xref:XamlToCSharpGenerator.Generator>

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
- [Testing and Validation](../advanced/testing-and-validation/)
- [Package and Assembly](package-and-assembly/)
