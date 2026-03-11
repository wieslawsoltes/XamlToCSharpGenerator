---
title: "Hot Reload and Hot Design Internals"
---

# Hot Reload and Hot Design Internals

This page focuses on the implementation side of AXSG hot reload and hot design.

## Runtime responsibilities

The runtime layers are responsible for:

- tracking generated type/source registrations
- maintaining include and theme registries
- mapping source paths back to generated types and resources
- coordinating UI-thread reload application
- preserving state where targeted reload can avoid full re-instantiation

## Compiler responsibilities

The compiler side must emit stable, deterministic artifacts so hot reload deltas do not destabilize Edit-and-Continue.

Recent work in this area includes:

- stable event binding helper names
- stable expression helper method emission
- deterministic include graph analysis
- explicit runtime helper generation instead of anonymous lambdas in emitted code

## Tooling responsibilities

Tooling layers coordinate the runtime and compiler through:

- `dotnet watch` graph integration
- mobile remote endpoint configuration
- language-service diagnostics and editor affordances for hot-reload-related markup surfaces

## Related docs

- [Docs and Release Infrastructure](docs-and-release-infrastructure/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)
- [iOS Hot Reload](../guides/hot-reload-ios/)
