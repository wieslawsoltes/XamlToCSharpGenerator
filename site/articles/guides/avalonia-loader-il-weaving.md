---
title: "Avalonia Loader Migration and IL Weaving"
---

# Avalonia Loader Migration and IL Weaving

AXSG can preserve common legacy Avalonia code-behind patterns by rewriting compiled `AvaloniaXamlLoader.Load(...)` calls to AXSG-generated initializer helpers during the build.

This is a migration bridge. It does not remove `AvaloniaXamlLoader` from Avalonia or change Avalonia's NuGet package surface. It changes your compiled application assembly so supported call sites end up on AXSG's generated object-graph path.

## Why this exists

Many Avalonia projects still contain class-backed code such as:

```csharp
private void InitializeComponent()
{
    AvaloniaXamlLoader.Load(this);
}
```

or:

```csharp
public ServiceProviderPanel(IServiceProvider serviceProvider)
{
    AvaloniaXamlLoader.Load(serviceProvider, this);
}
```

Before AXSG IL weaving, those call sites had to be removed or manually guarded during migration because they bypassed AXSG's generated `InitializeComponent(bool loadXaml = true)` path.

With IL weaving enabled, AXSG can keep those common source shapes working while still routing the compiled app through generated AXSG initialization.

## When the weaving pass runs

AXSG runs the weaving pass only when all of these are true:

- the AXSG backend is active
- the build is a real compile, not a design-time build
- the output assembly has been produced
- `XamlSourceGenIlWeavingEnabled` is `true`

The pass runs after `CoreCompile` and before output-copy targets, so the rewritten assembly is what the app and tests execute.

## Supported call shapes

AXSG rewrites these legacy call patterns when the target object is the current instance:

- `AvaloniaXamlLoader.Load(this);`
- `AvaloniaXamlLoader.Load(serviceProvider, this);`

The source spelling does not matter. Imported and fully-qualified forms both compile to the same IL call shape, so both are supported.

## What is not rewritten

AXSG does not rewrite every possible `AvaloniaXamlLoader` call. The current pass is intentionally narrow and deterministic.

These shapes are left alone:

- `AvaloniaXamlLoader.Load(otherInstance);`
- `var self = this; AvaloniaXamlLoader.Load(self);`
- call sites in non-AXSG builds
- call sites in design-time builds
- call sites on types that do not have AXSG-generated initializer helpers

The current rule is direct same-instance migration support, not arbitrary object-loader interception.

## What AXSG rewrites the calls to

For class-backed AXSG documents, the emitter now generates stable helper overloads:

- `__InitializeXamlSourceGenComponent(self)`
- `__InitializeXamlSourceGenComponent(serviceProvider, self)`

The weaver redirects supported `AvaloniaXamlLoader.Load(...)` calls to those helpers.

That means rewritten calls go through the same generated initialization body that powers AXSG class-backed XAML:

- generated object-graph population
- name rebinding
- service-provider-aware initialization
- hot reload and hot design registration

The weaving pass is therefore compatibility glue, not a second runtime path.

## Recommended migration paths

### Preferred long-term AXSG style

Once a project is fully migrated, the cleanest shape is still:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

with no hand-written `AvaloniaXamlLoader` wrapper at all.

### Compatibility-first migration

If you want to switch to AXSG without editing all existing code-behind immediately, keep the legacy wrapper temporarily and let AXSG rewrite it:

```csharp
public MainWindow()
{
    InitializeComponent();
}

private void InitializeComponent()
{
    AvaloniaXamlLoader.Load(this);
}
```

This also applies to `App.axaml.cs`.

### Explicit strict migration

If you want the build to enforce source cleanup instead of silently bridging it, disable weaving:

```xml
<PropertyGroup>
  <XamlSourceGenIlWeavingEnabled>false</XamlSourceGenIlWeavingEnabled>
</PropertyGroup>
```

With weaving disabled, the older AXSG rule comes back: a hand-written parameterless `InitializeComponent()` wrapper can intercept the constructor call and bypass the generated AXSG initializer.

## MSBuild properties

These switches are MSBuild-only build-integration properties. They do not map to `xaml-sourcegen.config.json`.

| Property | Default | Meaning |
| --- | --- | --- |
| `XamlSourceGenIlWeavingEnabled` | `true` | Canonical switch for the post-compile rewrite pass. |
| `AvaloniaSourceGenIlWeavingEnabled` | mirrors `XamlSourceGenIlWeavingEnabled` | Compatibility alias for existing Avalonia-prefixed configuration. |
| `XamlSourceGenIlWeavingStrict` | `true` | Fails the build when AXSG matches a supported loader call on a source-generated type but cannot find the generated initializer helper to rewrite to. |
| `AvaloniaSourceGenIlWeavingStrict` | mirrors `XamlSourceGenIlWeavingStrict` | Compatibility alias. |
| `XamlSourceGenIlWeavingVerbose` | `false` | Prints inspection, match, and rewrite counts for the pass. |
| `AvaloniaSourceGenIlWeavingVerbose` | mirrors `XamlSourceGenIlWeavingVerbose` | Compatibility alias. |
| `XamlSourceGenIlWeavingBackend` | `Metadata` | Selects the scan backend for the weaving pass. `Metadata` uses `System.Reflection.Metadata`; `Cecil` keeps the legacy Mono.Cecil scan path. |
| `AvaloniaSourceGenIlWeavingBackend` | mirrors `XamlSourceGenIlWeavingBackend` | Compatibility alias. |

Example:

```xml
<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
  <XamlSourceGenIlWeavingEnabled>true</XamlSourceGenIlWeavingEnabled>
  <XamlSourceGenIlWeavingVerbose>true</XamlSourceGenIlWeavingVerbose>
  <XamlSourceGenIlWeavingBackend>Metadata</XamlSourceGenIlWeavingBackend>
</PropertyGroup>
```

## Diagnostics and troubleshooting

When the pass rewrites anything, AXSG logs a build message like:

```text
[AXSG.Build] IL weaving inspected 11 type(s), matched 3 AvaloniaXamlLoader call(s), and rewrote 3 call(s) in '.../SourceGenIlWeavingSample.dll'.
```

Use `XamlSourceGenIlWeavingVerbose=true` when you want that message even for zero-rewrite builds.

If strict mode is enabled and AXSG finds a supported loader call on a source-generated type but cannot find the generated initializer helper, the build fails. That usually indicates one of these problems:

- the type is not actually using the expected AXSG-generated class-backed shape
- the call site does not match the supported same-instance pattern
- generated output is incomplete or stale

If the app still behaves like classic Avalonia loading, check:

1. `AvaloniaXamlCompilerBackend=SourceGen`
2. `XamlSourceGenIlWeavingEnabled=true`
3. the code-behind is using a supported call shape
4. the build output contains the AXSG IL weaving log

## Hot reload and hot design impact

Rewritten call sites land on the same generated AXSG initializer helpers used by the normal class-backed path, so hot reload and hot design registration stay intact.

The sample app at [`samples/SourceGenIlWeavingSample`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/tree/main/samples/SourceGenIlWeavingSample) covers:

- `App` initialization through `AvaloniaXamlLoader.Load(this)`
- view initialization through `AvaloniaXamlLoader.Load(this)`
- service-provider initialization through `AvaloniaXamlLoader.Load(serviceProvider, this)`

The corresponding runtime tests verify that rewritten calls still register tracked documents for hot reload.

## What this feature does not do

AXSG IL weaving does not:

- remove `AvaloniaXamlLoader` from the Avalonia package
- change Avalonia's default backend behavior when AXSG is not active
- rewrite arbitrary object-loader calls for non-current instances
- replace the recommended end-state of calling the generated AXSG `InitializeComponent()` directly

It is a build-time migration aid for supported legacy patterns.

## Related docs

- [InitializeComponent and Loader Fallback](../getting-started/initializecomponent-and-loader-fallback/)
- [Avalonia Name Generator and InitializeComponent Modes](avalonia-name-generator-and-initializecomponent-modes/)
- [Package: XamlToCSharpGenerator.Build](../reference/build/)
- [Configuration Model](../reference/configuration-model/)
- [Configuration Migration](../reference/configuration-migration/)
