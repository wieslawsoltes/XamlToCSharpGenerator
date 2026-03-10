---
title: "Runtime Loader and Fallback"
---

# Runtime Loader and Fallback

AXSG is designed to prefer generated code paths, but it also ships runtime fallback services for scenarios that still need them.

## Generated-first behavior

The normal app/runtime flow is:

1. compile XAML into generated C# and registry data
2. use generated lookup/runtime helpers at app startup and during hot reload
3. fall back only when the generated path is unavailable or a runtime-only action is being performed

## Runtime services

The runtime layers provide:

- source-info registries
- static resource resolution
- inline C# support helpers
- hot reload state and instance tracking
- runtime include/theme registries

## Why fallback still exists

Fallback behavior is still needed for:

- tooling and design surfaces
- hot reload diff application
- app startup in compatibility scenarios
- registry hydration and late-bound runtime helpers

## Related docs

- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload)
- [Resources, Includes, and URIs](../xaml/resources-includes-and-uris)
- [Hot Reload and Hot Design](../advanced/hot-reload-and-hot-design)
