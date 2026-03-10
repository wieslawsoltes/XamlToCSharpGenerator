---
title: "Generated Artifacts and Runtime"
---

# Generated Artifacts and Runtime

AXSG is not just a compiler. It produces generated C# plus runtime metadata and helper contracts that let the app, hot reload, and tooling understand the generated tree.

## Artifact categories

### Generated partial classes

These contain the object-graph construction code, initialization paths, helper methods for bindings/events, and generated partial hooks that match the authored XAML document.

### Runtime descriptors and registries

The compiler emits metadata that the runtime uses for:

- source-to-type mapping
- include/resource graph tracking
- hot reload state transfer
- name/source info lookup
- inline code and resource-resolution helpers

### Build and analyzer payloads

The generator and build targets are shipped separately from the runtime because compile-time and runtime responsibilities are distinct.

## Why the runtime exists at all

Pure generated code is not enough for the full AXSG feature set. The runtime layer is used for:

- hot reload and hot design coordination
- runtime fallback for unsupported or intentionally deferred paths
- source-info and type registries
- inline code/resource helpers that generated code can call deterministically

## Package split

- `XamlToCSharpGenerator.Runtime` is the umbrella runtime package
- `XamlToCSharpGenerator.Runtime.Core` contains framework-neutral contracts and registries
- `XamlToCSharpGenerator.Runtime.Avalonia` contains Avalonia-specific runtime integration

## Design rule

The runtime should support generated output; it should not replace the compiler. If a feature can be lowered deterministically into generated code, that path stays preferred over late runtime interpretation.

## Related docs

- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback.md)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload.md)
- [Package: XamlToCSharpGenerator.Runtime](../reference/packages/runtime.md)
- [Package: XamlToCSharpGenerator.Runtime.Core](../reference/packages/runtime-core.md)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](../reference/packages/runtime-avalonia.md)
