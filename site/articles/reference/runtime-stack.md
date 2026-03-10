---
title: "Runtime Stack Packages"
---

# Runtime Stack Packages

This guide explains the runtime-side package split, what lives in the neutral runtime layer versus the Avalonia layer, and when to stay on the umbrella package.

## Stack layout

| Layer | Packages | Purpose |
| --- | --- | --- |
| App-facing runtime surface | `XamlToCSharpGenerator.Runtime` | composition package for normal installs |
| Neutral runtime contracts | `XamlToCSharpGenerator.Runtime.Core` | registries, source info, hot reload/runtime contracts |
| Avalonia runtime integration | `XamlToCSharpGenerator.Runtime.Avalonia` | loader, resource resolution, inline-code runtime, hot reload integration |

## Recommended entry points

### Normal application usage

Use:

- [Package: XamlToCSharpGenerator](packages/xamltocsharpgenerator.md)
- [Package: XamlToCSharpGenerator.Runtime](packages/runtime.md)

This is the default install story. It keeps the runtime stack aligned with the build/generator stack without making you compose the pieces manually.

### Runtime host or infrastructure work

Use:

- [Package: XamlToCSharpGenerator.Runtime.Core](packages/runtime-core.md)

when you need only neutral registries/contracts and do not want the Avalonia layer.

### Avalonia runtime customization

Use:

- [Package: XamlToCSharpGenerator.Runtime.Avalonia](packages/runtime-avalonia.md)

when you need Avalonia loader behavior, resource/include resolution, inline-code runtime helpers, or hot reload integration directly.

## Responsibilities by layer

### `Runtime.Core`

Owns:

- source and type registries
- source-info and URI tracking
- hot reload/runtime coordination contracts
- framework-neutral descriptor/state helpers

Primary API entry points:

- <xref:XamlToCSharpGenerator.Runtime.XamlSourceGenRegistry>
- <xref:XamlToCSharpGenerator.Runtime.XamlSourceInfoRegistry>

### `Runtime.Avalonia`

Owns:

- generated loader integration
- runtime XAML fallback hooks
- include/resource lookup in Avalonia hosts
- inline `CSharp` runtime helpers
- hot reload and hot design integration

Primary API entry points:

- <xref:XamlToCSharpGenerator.Runtime.AvaloniaSourceGeneratedXamlLoader>
- <xref:XamlToCSharpGenerator.Runtime.Markup.CSharp>

### `Runtime`

Is intentionally narrative-first. It is the package-level install surface, not the public API destination. When you need member-level API, go to the subpackages.

## Related docs

- [Runtime and Editor Namespaces](namespace-runtime-and-editor.md)
- [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime.md)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback.md)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload.md)
