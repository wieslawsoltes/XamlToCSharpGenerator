---
title: "Generated Artifacts and Runtime Contracts"
---

# Generated Artifacts and Runtime Contracts

AXSG produces generated partial classes plus runtime metadata used by hot reload, tooling, and fallback loaders.

## Generated artifacts

Typical outputs include:

- generated partial class initialization code
- compiled binding registration helpers
- event binding wrappers
- source info / registry metadata when enabled
- hot reload tracking artifacts

## Runtime layers

The runtime is intentionally split:

- `XamlToCSharpGenerator.Runtime.Core`
  - framework-agnostic registries and URI/source tracking
- `XamlToCSharpGenerator.Runtime.Avalonia`
  - Avalonia-specific loader/bootstrap/hot reload behavior
- `XamlToCSharpGenerator.Runtime`
  - compatibility umbrella package over the two runtime layers
