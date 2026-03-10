---
title: "Package: XamlToCSharpGenerator.Runtime.Core"
---

# XamlToCSharpGenerator.Runtime.Core

## Role

Framework-neutral runtime registries, URI mapping, source info, and hot reload contracts.

## Related namespaces

- <xref:XamlToCSharpGenerator.Runtime>

## Use it when

- you need runtime registries/contracts without Avalonia integration
- you are integrating AXSG runtime behavior into another host

## What ships here

This package contains the framework-neutral parts of the runtime layer:

- source path and type registries
- URI/source info tracking
- hot reload coordination contracts
- runtime state transfer helpers that do not depend on Avalonia

This layer keeps the runtime contract stable even when the Avalonia-facing runtime changes. It is what makes the runtime model reusable rather than hardcoded into a single UI framework implementation.

## Typical consumers

- `XamlToCSharpGenerator.Runtime`
- `XamlToCSharpGenerator.Runtime.Avalonia`
- custom runtime hosts that need registry and source-info primitives without Avalonia-specific integration

## Use it directly when

- you are testing registry or source-info behavior without Avalonia
- you are reusing AXSG runtime concepts in another host
- you need the neutral contract layer beneath the Avalonia runtime

## Related docs

- [Generated Artifacts and Runtime](../concepts/generated-artifacts-and-runtime/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)
- [Internal Support Components](../internal-support-components/)
