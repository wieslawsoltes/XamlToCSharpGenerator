---
title: "Compiler and Core Namespaces"
---

# Compiler and Core Namespaces

This namespace family covers the generic compiler host, the stable semantic models it consumes, and the framework-agnostic contracts that profile implementations build on.

## Packages behind this area

- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Framework.Abstractions`

## Primary namespaces

- <xref:XamlToCSharpGenerator.Compiler>
- <xref:XamlToCSharpGenerator.Core.Abstractions>
- <xref:XamlToCSharpGenerator.Core.Configuration>
- <xref:XamlToCSharpGenerator.Core.Configuration.Sources>
- <xref:XamlToCSharpGenerator.Core.Diagnostics>
- <xref:XamlToCSharpGenerator.Core.Models>
- <xref:XamlToCSharpGenerator.Core.Parsing>
- <xref:XamlToCSharpGenerator.Framework.Abstractions>

## What lives here

### Compiler host orchestration

The `Compiler` namespace owns project discovery, include-graph analysis, configuration precedence, transform-rule merging, and the transition into a chosen framework profile.

### Shared contracts and models

The `Core` namespaces define the semantic contracts reused across compiler, runtime, and language service layers:

- diagnostics
- document and configuration models
- binding, event, resource, and property-element abstractions
- parser/token semantics used in multiple subsystems

### Framework extension seams

`Framework.Abstractions` provides the profile hooks that let AXSG target Avalonia today and alternative profiles in the future.

## Use this area when

- you are extending the compiler host
- you are debugging configuration or include-graph behavior
- you need the canonical diagnostic/model definitions used by several layers
- you are building or evaluating another framework profile

## Suggested API entry points

- <xref:XamlToCSharpGenerator.Compiler.XamlSourceGeneratorCompilerHost>
- <xref:XamlToCSharpGenerator.Core.Models.ResolvedCompiledBindingDefinition>
- <xref:XamlToCSharpGenerator.Core.Diagnostics.DiagnosticCatalog>
- <xref:XamlToCSharpGenerator.Framework.Abstractions.IXamlFrameworkProfile>

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model)
- [Compiler Pipeline](../architecture/compiler-pipeline)
- [Configuration Model](configuration-model)
- [Package: XamlToCSharpGenerator.Compiler](packages/compiler)
