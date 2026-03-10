---
title: "Runtime and Hot Reload"
---

# Runtime and Hot Reload

AXSG hot reload is not a separate product bolted onto the compiler. It depends on generated output shape, runtime registries, source mapping, and `dotnet watch`/IDE transport coordination.

## Main pieces

### Generated output

The compiler emits stable helper methods, registries, and metadata so runtime reload can identify and update the correct generated surface.

### Runtime registries

The runtime tracks:

- type-to-source mappings
- include graphs
- runtime source info
- hot reload state transfer
- known type URIs and resource relationships

### Transport layer

The runtime can participate in:

- `MetadataUpdateHandler`-based managed updates
- `dotnet watch` / proxy-assisted flows
- platform-specific hot reload workflows, including iOS-related paths

## Why stability matters

Edit-and-continue and hot reload are sensitive to generated method identity. That is why AXSG stabilizes generated helper names for:

- event bindings
- inline event code
- expression binding helpers

Without that, harmless XAML edits can destabilize Roslyn delta metadata and crash the watch flow.

## Operational boundaries

- compiler semantics stay authoritative
- runtime helpers support reload and execution
- hot reload should preserve state when possible, but not by making generated output nondeterministic

## Related docs

- [Hot Reload and Hot Design](../guides/hot-reload-and-hot-design.md)
- [iOS Hot Reload](../guides/hot-reload-ios.md)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback.md)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](../reference/packages/runtime-avalonia.md)
