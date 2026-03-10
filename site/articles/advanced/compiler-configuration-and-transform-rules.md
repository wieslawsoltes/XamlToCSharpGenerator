---
title: "Compiler Configuration and Transform Rules"
---

# Compiler Configuration and Transform Rules

AXSG merges configuration from multiple sources before the binder/emitter pipeline runs.

## Configuration sources

The compiler host can read configuration from:

- MSBuild items and properties
- file-based configuration documents
- transform-rule documents
- framework/profile defaults

## Precedence

Configuration is not merged arbitrarily. AXSG applies explicit precedence rules so the final effective configuration is deterministic.

This matters for:

- global XML namespace mappings
- string-to-type alias tables
- transform rules
- backend and profile behavior

## Transform rules

Transform-rule documents let the host converge project-specific conventions into compiler decisions without hardcoding those rules into the stable compiler core.

This is the main extension point for:

- namespace remapping
- type aliasing
- feature-shape convergence across projects
- migration layers from other markup/codegen conventions

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model)
- [Configuration Model](../reference/configuration-model)
- [Configuration Migration](../reference/configuration-migration)
