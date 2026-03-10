---
title: "Conditional XAML"
---

# Conditional XAML

AXSG supports conditional inclusion/exclusion of XAML branches at compile time so one source document can adapt to build configuration, platform, or feature flags without pushing that complexity into runtime logic.

## What conditional XAML is for

Use it when you need to:

- include platform-specific markup only for certain targets
- prune feature branches from generated output in CI or release builds
- keep one source document while changing authored markup by build state

## Design expectations

Conditional XAML should remain predictable:

- conditions are resolved during compilation
- excluded nodes do not survive into generated output
- diagnostics should point at the authored XAML, not just the lowered result

## Interaction with the rest of the compiler

Conditional evaluation happens before later stages rely on the surviving document tree. That means it affects:

- generated object graph shape
- include/resource relationships
- binding/selector analysis on the remaining nodes
- language-service visibility if the current configuration is reflected in the active compilation

## When not to use it

Do not use conditional XAML to replace ordinary runtime state or view-model-driven branching. If the branch depends on application state, use normal bindings/templates instead.

## Related docs

- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules)
- [Configuration Model](../reference/configuration-model)
- [Compiler Pipeline](../architecture/compiler-pipeline)
