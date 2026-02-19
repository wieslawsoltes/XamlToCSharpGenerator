# Avalonia.Markup.Declarative Hot Reload Analysis

## Scope
This document analyzes how hot reload is implemented in:
- `/Users/wieslawsoltes/GitHub/Avalonia.Markup.Declarative/src/Avalonia.Markup.Declarative/HotReloadManager.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia.Markup.Declarative/src/Avalonia.Markup.Declarative/ViewBase.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia.Markup.Declarative/src/Avalonia.Markup.Declarative/IReloadable.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia.Markup.Declarative/src/Avalonia.Markup.Declarative/CommonExtensions/AppBuilderExtensions.cs`

## High-level architecture
Avalonia.Markup.Declarative uses native .NET metadata-update callbacks plus per-instance UI rebuilding:

1. Assembly-level metadata update hook:
   - `[assembly: MetadataUpdateHandler(typeof(HotReloadManager))]`
   - This causes .NET hot reload to call `ClearCache(Type[]?)` and `UpdateApplication(Type[]?)`.

2. Instance registry by concrete type:
   - `ConcurrentDictionary<Type, HashSet<IReloadable>> Instances`
   - Views register on attach (`OnAttachedToVisualTree`) and unregister on detach.

3. Reload target abstraction:
   - `IReloadable.Reload()` provides the operation executed on changed live instances.

4. Per-instance UI-thread reload lifecycle in `ViewBase.Reload()`:
   - run on Avalonia UI dispatcher
   - remove subscriptions/computed state
   - clear child visual/content and namescope
   - reinitialize view content/build tree
   - restore data context and invalidate layout/visual

5. App opt-in toggle:
   - `UseHotReload(enable)` toggles runtime activation.

6. Rider workaround path:
   - Optional polling of method body token changes with a timer.
   - Disabled automatically when native metadata updates are detected.

## Implementation details that matter

### MetadataUpdateHandler contract
`HotReloadManager` exposes both required static methods:
- `ClearCache(Type[]? types)`
- `UpdateApplication(Type[]? types)`

`UpdateApplication` is the real trigger and dispatches reload to tracked instances for changed types.

### Instance tracking strategy
- Registry key is the runtime type (generic types normalized to generic type definition).
- Registry values are live `IReloadable` instances.
- Tracked lifecycle is visual-tree attachment based.

### Reload lifecycle strategy
`Reload()` in `ViewBase` is idempotent-oriented and state-preserving:
- cleans old listeners/derived state
- clears prior visual graph
- resets namescope and init flags
- re-runs initialization/build
- restores context and invalidates measure/arrange/visual

This is the critical behavior that enables repeated hot updates without requiring app restart.

## Strengths
1. Uses native .NET hot reload mechanism (no custom protocol required).
2. Targeted reload by changed types.
3. View-level lifecycle is explicit and relatively deterministic.
4. Can be disabled globally.

## Weaknesses / risks
1. `HashSet<IReloadable>` per type is not weak-reference based; detached instances rely on unregister correctness.
2. Reload operation assumes control over visual rebuild semantics; custom controls may need overrides (`OnBeforeReload`).
3. Rider workaround depends on method-body metadata token heuristic and is best-effort.

## Applicability to XamlToCSharpGenerator
The reusable pattern is:
1. Add runtime `MetadataUpdateHandler` bridge.
2. Track live generated instances by generated root type.
3. Provide per-generated-type reload delegate that re-runs generated graph population in UI thread.
4. Make generated population idempotent (clear-before-add, safe event rewire).

For our AXAML source generator, this maps cleanly to generated `InitializeComponent` and generated `__PopulateGeneratedObjectGraph` methods.
