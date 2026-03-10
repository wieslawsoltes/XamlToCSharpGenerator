---
title: "Hot Reload and Hot Design"
---

# Hot Reload and Hot Design

AXSG hot reload is layered on top of generated artifacts instead of only relying on generic runtime XAML loading.

## Supported flows

- desktop hot reload through `dotnet watch`
- mobile hot reload, including iOS transport selection and remote endpoint support
- runtime source reload fallback for supported file types
- hot-design/runtime inspection support in the editor/runtime layer

## Design constraints

Hot reload stays reliable only if generated code remains stable across edits.

That is why recent work focused on:

- stable event wrapper method identities
- stable expression-binding helper emission
- deterministic registry and include-graph output
- runtime helper surfaces that avoid reflection and implicit state

## Related docs

- [iOS Hot Reload](hot-reload-ios)
- [Runtime Loader and Fallback](runtime-loader-and-fallback)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload)
