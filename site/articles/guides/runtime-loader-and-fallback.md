---
title: "Runtime Loader and Fallback"
---

# Runtime Loader and Fallback

AXSG prefers deterministic generated code, but the runtime still has a defined role when generated output needs support services or a fallback path.

## Normal path

The normal execution path is:

1. XAML is compiled into generated C#.
2. Generated partial classes build the object graph.
3. Runtime helpers provide registries, source info, hot reload plumbing, and resource helpers where required.

## Fallback path

Fallback is used when a scenario is intentionally handled at runtime rather than fully lowered into generated code. Examples include:

- runtime loader support for targeted scenarios
- resource/include resolution helpers
- hot reload state transfer and source mapping
- limited runtime-side handling for features that are not fully reduced to generated code

## Design rule

Fallback should never become the default design for normal compiler behavior. If a scenario can be emitted deterministically into generated code, AXSG should prefer that route and keep fallback as operational support rather than semantic authority.

## Class-backed XAML is not a runtime-loader fallback case

For class-backed Avalonia XAML, AXSG generates `InitializeComponent(bool loadXaml = true)` into the generated partial type. That generated method is the normal class initialization path.

The AXSG runtime bootstrap and fallback services do not replace that method. In particular:

- `.UseAvaloniaSourceGeneratedXaml()` does not make an old hand-written `InitializeComponent()` wrapper safe
- a parameterless `InitializeComponent()` that still calls `AvaloniaXamlLoader.Load(this)` can bypass the generated AXSG method completely

If you are migrating an existing Avalonia app, use the guarded fallback pattern documented here:

- [InitializeComponent and Loader Fallback](../getting-started/initializecomponent-and-loader-fallback/)

## Relevant runtime packages

- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`

## Related feature areas

- resources, includes, and URI resolution
- hot reload and hot design
- source/type registries
- inline code helpers and markup runtime support

## Related docs

- [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload/)
- [Resources, Includes, and URIs](../xaml/resources-includes-and-uris/)
