---
title: "Concepts"
---

# Concepts

Use this section when you need the model behind AXSG rather than a package-install recipe. These pages explain how the compiler thinks about documents, bindings, generated artifacts, and tooling.

## Why this section matters

AXSG is not a single library with one execution path. The same authored XAML feature can span:

- compiler-host discovery and configuration precedence
- framework-profile-specific binding and emission rules
- runtime registries and helper contracts
- language-service reuse of the exact same semantic model

If you only read package or API pages, those moving parts can look unrelated. The concept pages explain why they exist and how they fit together.

## Recommended reading order

1. [Compiler Host and Project Model](compiler-host-and-project-model/)
   : how AXSG discovers XAML, configuration, includes, and transform inputs.
2. [Binding and Expression Model](binding-and-expression-model/)
   : how bindings, shorthand, inline C#, and event semantics are lowered.
3. [Generated Artifacts and Runtime Contracts](generated-artifacts-and-runtime/)
   : what the compiler emits and why the runtime needs registries and descriptors.
4. [Tooling Surface](tooling-surface/)
   : how compiler semantics are projected into the language service, LSP host, and editors.
5. [Glossary](glossary/)
   : the common terms used across docs, diagnostics, tests, and API summaries.

## Use this section when

- you are investigating a diagnostic and need to know which stage produced it
- you are extending AXSG and need to understand the stable contracts between host, profile, runtime, and tooling
- you are reading generated code or runtime descriptors and want to know why they exist
- you are trying to line up narrative docs with the API reference

## Related sections

- [Getting Started](../getting-started/)
- [Guides](../guides/)
- [Architecture](../architecture/)
- [Reference](../reference/)
