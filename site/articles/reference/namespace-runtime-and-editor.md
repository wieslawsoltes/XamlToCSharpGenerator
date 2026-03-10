---
title: "Runtime and Editor Namespaces"
---

# Runtime and Editor Namespaces

This namespace family covers generated-object runtime support, source-info and reload registries, inline markup helpers, and the in-app editor surface.

## Packages behind this area

- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Core`
- `XamlToCSharpGenerator.Runtime.Avalonia`
- `XamlToCSharpGenerator.Editor.Avalonia`

## Primary namespaces

- <xref:XamlToCSharpGenerator.Runtime>
- <xref:XamlToCSharpGenerator.Runtime.Markup>
- <xref:XamlToCSharpGenerator.Editor.Avalonia>

## What lives here

### Runtime registries and contracts

`Runtime` and `Runtime.Core` own the framework-neutral registry and source-info contracts used for hot reload, source mapping, generated type lookup, and runtime fallback.

### Avalonia runtime integration

`Runtime.Avalonia` adds Avalonia-specific loader behavior, resource resolution, inline-code runtime helpers, and mobile/desktop hot reload integration.

### In-process editor hosting

`Editor.Avalonia` belongs here because it is an editor host that sits directly on top of runtime/editor-friendly language-service services for in-app authoring tools.

## Use this area when

- you are debugging runtime fallback or generated loader behavior
- you are investigating source-info and include registry behavior
- you are working on hot reload transports or state transfer
- you are embedding an AXAML editor into an Avalonia application

## Suggested API entry points

- <xref:XamlToCSharpGenerator.Runtime.XamlSourceGenRegistry>
- <xref:XamlToCSharpGenerator.Runtime.Markup.CSharp>
- <xref:XamlToCSharpGenerator.Runtime.AvaloniaSourceGeneratedXamlLoader>
- <xref:XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor>

## Related docs

- [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](runtime-avalonia/)
