---
title: "Hot Reload and Hot Design"
---

# Hot Reload and Hot Design

AXSG hot reload is built around generated artifacts, runtime registries, and stable helper identities. It is not just a generic XAML text reload story.

## What AXSG hot reload supports

- desktop `dotnet watch` workflows over generated C# output
- runtime source reload fallback for supported XAML/resource cases
- mobile transport support, including iOS scenarios that need explicit transport coordination
- hot-design/runtime-inspection surfaces used by in-app editors and design tools

## Why generated stability matters

The hot reload path depends on Roslyn Edit-and-Continue generating valid deltas. That only works when generated members remain stable across harmless XAML edits.

AXSG explicitly stabilizes:

- inline event wrapper method identities
- expression helper emission
- include graph and generated URI identities
- runtime descriptor keys used by reload and source-info registries

Without that stability, `dotnet watch` can fail with duplicate metadata rows or invalid delta generation.

## Runtime pieces involved

The runtime layer participates through:

- source path and type registries
- include/resource registries
- runtime loader and fallback helpers
- inline-code and event helper services
- hot-design and selection/inspection services in editor-facing scenarios

## Typical workflows

### Normal desktop authoring

1. start the app with `dotnet watch`
2. edit XAML, resources, or supported inline code
3. let AXSG regenerate deterministic helper output
4. apply metadata updates or runtime fallback updates as appropriate

### Resource/include-heavy editing

AXSG tracks include graphs and effective XAML URIs, so reload can target the right resource dictionary or included file instead of only the physical file path.

### Mobile or remote sessions

On iOS and similar workflows, the transport path matters. AXSG can use metadata-update or remote/proxy-backed paths depending on how the app and host are started.

## Failure modes to recognize

### EnC duplicate-key or duplicate-row crashes

Usually caused by unstable generated helper identities. Check recent compiler changes around:

- event binding wrapper emission
- expression helper emission
- generated method naming

### Runtime fallback logs instead of metadata updates

This usually means the changed surface is supported by the fallback loader but not by the current metadata-update path.

### State loss after reload

This is usually a runtime replacement/state-transfer issue, not a parser or language-service issue. Inspect the runtime registry and replacement path.

## Operational checklist

- keep generated helper names deterministic
- validate `dotnet watch` on at least one real sample app after changing compiler emission
- verify include/resource reload against linked XAML items, not just physical paths
- treat mobile transport selection as part of the feature, not as a separate infrastructure problem

## Related docs

- [iOS Hot Reload](hot-reload-ios.md)
- [Runtime Loader and Fallback](runtime-loader-and-fallback.md)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload.md)
- [Troubleshooting](troubleshooting.md)
