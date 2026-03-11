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

1. [Compiler Configuration and Transform Rules](compiler-configuration-and-transform-rules/)
2. [Custom Framework Profiles](custom-framework-profiles/)
3. [Hot Reload and Hot Design Internals](hot-reload-and-hot-design/)
4. [Language Service and Compiler Performance](language-service-and-compiler-performance/)
5. [Testing and Validation](testing-and-validation/)
6. [Docs and Release Infrastructure](docs-and-release-infrastructure/)

## Typical scenarios

### You are adding a new framework profile

Start with:

- [Custom Framework Profiles](custom-framework-profiles/)
- [Compiler Pipeline](../architecture/compiler-pipeline/)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)

### You are changing build/configuration behavior

Start with:

- [Compiler Configuration and Transform Rules](compiler-configuration-and-transform-rules/)
- [Configuration Model](../reference/configuration-model/)
- [Configuration Migration](../reference/configuration-migration/)

### You are debugging hot reload or runtime state transfer

Start with:

- [Hot Reload and Hot Design Internals](hot-reload-and-hot-design/)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)

### You are tuning performance or CI throughput

Start with:

- [Language Service and Compiler Performance](language-service-and-compiler-performance/)
- [Performance and Benchmarking](performance-and-benchmarking/)
- [Testing and Validation](testing-and-validation/)

### You are maintaining docs or the release pipeline

Start with:

- [Docs and Release Infrastructure](docs-and-release-infrastructure/)
- [Packaging and Release](../guides/packaging-and-release/)
- [Lunet Docs Pipeline](../reference/lunet-docs-pipeline/)

## Cross-links

- [Architecture](../architecture/)
- [Reference](../reference/)
- [Package Guides](../reference/package-guides/)
