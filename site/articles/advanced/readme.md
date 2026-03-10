---
title: "Advanced"
---

# Advanced

This section is for extension authors, package maintainers, and teams integrating AXSG into a larger product or build platform.

Use these articles when you need to move beyond the default application-facing install path and reason about the internals that shape generated output, runtime behavior, language tooling, docs publication, and release automation.

## What this section covers

- compiler configuration layering and transform-rule convergence
- custom framework profile design and implementation boundaries
- runtime hot reload and hot design behavior under real build/watch workflows
- language-service and compiler performance work, including benchmark expectations
- test strategy across parser, compiler, runtime, language service, and UI layers
- docs generation, packaging, and release automation

## Recommended reading order

1. [Compiler Configuration and Transform Rules](compiler-configuration-and-transform-rules.md)
2. [Custom Framework Profiles](custom-framework-profiles.md)
3. [Hot Reload and Hot Design Internals](hot-reload-and-hot-design.md)
4. [Language Service and Compiler Performance](language-service-and-compiler-performance.md)
5. [Testing and Validation](testing-and-validation.md)
6. [Docs and Release Infrastructure](docs-and-release-infrastructure.md)

## Typical scenarios

### You are adding a new framework profile

Start with:

- [Custom Framework Profiles](custom-framework-profiles.md)
- [Compiler Pipeline](../architecture/compiler-pipeline.md)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model.md)

### You are changing build/configuration behavior

Start with:

- [Compiler Configuration and Transform Rules](compiler-configuration-and-transform-rules.md)
- [Configuration Model](../reference/configuration-model.md)
- [Configuration Migration](../reference/configuration-migration.md)

### You are debugging hot reload or runtime state transfer

Start with:

- [Hot Reload and Hot Design Internals](hot-reload-and-hot-design.md)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload.md)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback.md)

### You are tuning performance or CI throughput

Start with:

- [Language Service and Compiler Performance](language-service-and-compiler-performance.md)
- [Performance and Benchmarking](performance-and-benchmarking.md)
- [Testing and Validation](testing-and-validation.md)

### You are maintaining docs or the release pipeline

Start with:

- [Docs and Release Infrastructure](docs-and-release-infrastructure.md)
- [Packaging and Release](../guides/packaging-and-release.md)
- [Lunet Docs Pipeline](../reference/lunet-docs-pipeline.md)

## Cross-links

- [Architecture](../architecture/)
- [Reference](../reference/)
- [Package Guides](../reference/packages/)
