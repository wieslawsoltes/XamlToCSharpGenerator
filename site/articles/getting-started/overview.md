---
title: "Why AXSG"
---

# Why AXSG

AXSG compiles Avalonia XAML into generated C# and then layers runtime, language-service, and editor tooling on top of that compiler. The goal is to keep XAML authoring productive without treating XAML as a reflection-only runtime artifact.

## What AXSG changes

Compared with a plain XAML pipeline, AXSG emphasizes:

- generated C# output you can inspect, diff, debug, and reason about
- stronger semantic checking for bindings, selectors, themes, includes, and resource references
- C# expression features that still stay inside valid XAML forms
- runtime support for hot reload, hot design, and generated fallback services
- editor features that understand compiler semantics instead of only XML structure

## Where it fits

AXSG is useful when you need one or more of these:

- compile-time validation for XAML-heavy UI codebases
- generated code you can audit or profile
- richer binding/expression syntax without code-behind event handlers
- editor tooling that follows the same semantics as the build
- runtime support for hot reload and design-time workflows

## Main feature families

### XAML language features

- compiled bindings with explicit and inferred source context
- shorthand expressions and full expression bindings
- inline C# in attribute, object-element, and CDATA forms
- event bindings and inline event code
- selector, control-theme, resource, include, and URI navigation support

### Runtime features

- generated helper/runtime contracts
- hot reload registration and replacement logic
- hot design and registry-backed tooling surfaces
- runtime fallback services where generated code delegates execution

### Tooling features

- LSP-based completion, hover, definition, declaration, references, rename, inlay hints, and semantic tokens
- cross-language navigation between XAML and C#
- projected inline-C# interop for existing editor-side C# providers where useful

## When not to start with the umbrella package

The umbrella package is the right default for applications. Start with lower-level packages only if you are:

- building a custom host or framework profile
- embedding the language service/editor pieces separately
- consuming only runtime or build integration surfaces

Use [Package Selection and Integration](../guides/package-selection-and-integration) when you need that split.

## Next steps

- [Installation](installation)
- [Quickstart](quickstart)
- [Artifact Matrix](../reference/artifact-matrix)
- [Compiler Pipeline](../architecture/compiler-pipeline)
