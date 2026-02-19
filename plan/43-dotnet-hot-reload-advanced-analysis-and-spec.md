# DotNet Hot Reload: Advanced Analysis and Spec

Date: 2026-02-20

## Scope
Analyze existing SourceGen hot reload in this repository and define a robust, phased runtime pipeline.

## Baseline Analysis

### Existing implementation before this wave
1. Metadata entry points existed:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotReloadManager.cs`
   - `UpdateApplication(Type[]?)`
   - `ClearCache(Type[]?)`
2. Runtime tracked instances with weak references and reloaded by type.
3. IDE fallback existed (method-token + source-path polling).
4. Gaps:
   - No replacement-type to original-type mapping.
   - No serialized queue for reentrant updates.
   - No formal phased extension hooks.
   - Limited state transfer beyond reload delegate.

## Architecture Decisions

### 1) Keep callback contract, add typed pipeline context
1. Preserve:
   - `UpdateApplication(Type[]?)`
   - `ClearCache(Type[]?)`
2. Add:
   - `SourceGenHotReloadUpdateContext`
   - `SourceGenHotReloadTrigger`

### 2) Add replacement-type mapping
1. Resolve `System.Runtime.CompilerServices.MetadataUpdateOriginalTypeAttribute` from updated types.
2. Map replacement types to original tracked types before operation collection.
3. Cache mappings for stable behavior during a debug session.

### 3) Serialize and queue updates
1. Add in-flight reload gate.
2. Queue reentrant/concurrent update requests.
3. Drain queue after active pass completes.

### 4) Add phased extension hooks
1. New handler contract:
   - `ISourceGenHotReloadHandler`
2. New assembly-level registration attribute:
   - `SourceGenHotReloadHandlerAttribute`
3. Supported phases:
   - `BeforeVisualTreeUpdate`
   - `CaptureState`
   - `BeforeElementReload`
   - `AfterElementReload`
   - `AfterVisualTreeUpdate`
   - `ReloadCompleted`

### 5) Add registration-level state transfer
1. Introduce `SourceGenHotReloadRegistrationOptions`.
2. Extend register API with:
   - `BeforeReload`
   - `CaptureState`
   - `RestoreState`
   - `AfterReload`
   - `SourcePath`

### 6) Preserve UI-thread safety
1. Execute reload pipeline on Avalonia UI dispatcher when required.
2. Keep non-UI object reloads callable without dispatcher.

### 7) Preserve IDE fallback compatibility
1. Keep polling fallback and source-path retry behavior.
2. Refresh watcher snapshots after each pipeline pass.

## Public Contract Updates
1. New runtime APIs:
   - `XamlSourceGenHotReloadManager.Register(..., SourceGenHotReloadRegistrationOptions?)`
   - `XamlSourceGenHotReloadManager.RegisterHandler(ISourceGenHotReloadHandler, Type? elementType = null)`
   - `XamlSourceGenHotReloadManager.ResetHandlersToDefaults()`
2. New runtime events:
   - `HotReloadPipelineStarted`
   - `HotReloadPipelineCompleted`
   - `HotReloadHandlerFailed`
3. New AppBuilder extension:
   - `UseAvaloniaSourceGeneratedXamlHotReloadHandler(...)`

## Test Plan
1. Mapping behavior:
   - replacement-type update reloads original tracked type.
2. Queue robustness:
   - reentrant updates are queued and replayed.
3. Phase + state behavior:
   - registration capture/restore and handler phases execute in expected lifecycle.
4. Assembly handler loading:
   - assembly-level handler registration gets discovered and invoked.
5. Generator/runtime integration:
   - generated registration uses options with state capture/restore.

## Diagnostics Plan
1. Preserve:
   - `HotReloadFailed`
   - trace stream `AXSG_HOTRELOAD_TRACE`
2. Add:
   - `HotReloadHandlerFailed` for phase-level handler failures.
   - pipeline lifecycle events with trigger + operation count context.

## Acceptance Criteria
1. Runtime reload works when callback provides replacement type.
2. Reentrant updates do not overlap reload passes.
3. Phased extensibility + state transfer are available and test-covered.
4. Existing `dotnet watch` and IDE fallback behavior remains functional.
