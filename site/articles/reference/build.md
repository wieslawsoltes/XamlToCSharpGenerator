---
title: "Package: XamlToCSharpGenerator.Build"
---

# XamlToCSharpGenerator.Build

## Role

Provides the MSBuild props/targets layer that wires AXSG into project builds, backend selection, docs packaging, local analyzer deployment, and build-time runtime integration.

## What ships

This package is a build-transitive shell. It does not expose a user-facing code assembly. Instead it contributes:

- `buildTransitive/XamlToCSharpGenerator.Build.props`
- `buildTransitive/XamlToCSharpGenerator.Build.targets`

Those files coordinate:

- backend selection
- generator/runtime asset wiring
- local analyzer deployment for repo development
- docs packaging hooks
- hot-reload-related build behavior

This package is where package selection becomes real build behavior. It is the import layer that decides which generator/runtime pieces are wired into the consumer graph and how repo-local development differs from packaged consumption.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.Build" Version="<VERSION>" />
```

## Typical consumers

- SDK/build integrators who need AXSG build behavior without the umbrella package
- repos that want to override or inspect the imported targets directly
- CI/release flows that package docs or generated artifacts as part of the same build

## Common scenarios

Use this package directly when you are:

- authoring a custom SDK or template
- debugging `buildTransitive` behavior
- integrating AXSG into a repo with its own target layering
- maintaining CI/release/docs workflows that rely on shared build imports

## What it is not

This package does not contain binding, selector, runtime, or editor semantics. It is an integration layer, not the implementation layer for those features.

## Use it when

- you need explicit build integration hooks
- you are authoring custom SDK or project wiring
- you need backend-switch or target import behavior without taking the umbrella package

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules/)
- [Packaging and Release](../guides/packaging-and-release/)
- [Package and Assembly](package-and-assembly/)
- [Package Selection and Integration](../guides/package-selection-and-integration/)
