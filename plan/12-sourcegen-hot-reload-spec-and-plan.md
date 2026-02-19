# SourceGen Hot Reload Spec and Execution Plan

## Objective
Add full runtime hot reload support for generated AXAML C# output using native .NET metadata updates, so live source-generated views are re-applied without restarting the app.

## Scope (v1)
In scope:
1. Native metadata update integration for generated views/windows/user controls.
2. Live-instance registry and targeted reload by updated CLR type.
3. Generated reload delegate and idempotent regenerate/apply flow.
4. AppBuilder API for enabling/disabling sourcegen hot reload.
5. Build/test coverage for generator/runtime behavior.

Out of scope (v1):
1. Hot reload for non-generated dynamic XAML runtime compilation.
2. IDE-specific fallback polling (Rider workaround equivalent).
3. State diffing; v1 does full generated graph re-apply per instance.

## Architecture specification

### 1. Runtime manager
Add `XamlSourceGenHotReloadManager` in runtime package with:
1. `[assembly: MetadataUpdateHandler(typeof(...))]` attribute.
2. Public static methods:
   - `ClearCache(Type[]? types)`
   - `UpdateApplication(Type[]? types)`
3. Global enable flag and public toggle API.
4. Live weak-instance registry keyed by view type.
5. Per-type reload delegates (`Action<object>`) registered by generated code.
6. UI-thread dispatch for reload invocation.

### 2. Generator integration
Generated class must include:
1. A reload method:
   - `internal void __ApplySourceGenHotReload()`
   - calls `__PopulateGeneratedObjectGraph(this)`
   - invalidates layout/visual when supported
2. Registration in `InitializeComponent` when sourcegen path is used:
   - register instance + type + reload delegate with runtime manager.

### 3. Idempotent graph population requirement
`__PopulateGeneratedObjectGraph` must be safe for repeated invocation:
1. Collection-like child targets cleared before add.
2. Dictionary-like targets cleared before keyed add.
3. Event subscriptions re-bound safely (remove then add).
4. Property-element collection assignment also clear-before-add.

### 4. AppBuilder contract
Add runtime extension:
1. `UseAvaloniaSourceGeneratedXamlHotReload(this AppBuilder builder, bool enable = true)`
2. `UseAvaloniaSourceGeneratedXaml(...)` should keep loader enable behavior and turn on hot reload by default for sourcegen path.

### 5. Diagnostics and observability
Runtime manager should expose:
1. hot-reload event callback (`HotReloaded`) for diagnostics.
2. robust exception handling around reload delegate execution.

## Compatibility and safety constraints
1. No reflection in compiler path (generator/binder/emitter); runtime may use simple object/interface checks only.
2. Works on net6+/net8+ target frameworks in runtime package.
3. No change to default XamlIl backend behavior.

## Execution plan

### Phase A: Runtime foundation
1. Add manager + metadata update handler.
2. Add weak-instance tracking and registration API.
3. Add AppBuilder extension toggles.

### Phase B: Generator reload wiring
1. Emit per-class `__ApplySourceGenHotReload` method.
2. Register live instances in `InitializeComponent` sourcegen path.

### Phase C: Idempotence hardening
1. Clear collection/dictionary-like targets before adds.
2. Make event hookups idempotent.

### Phase D: Tests and validation
1. Generator tests for emitted hot-reload methods/registration/clear patterns.
2. Runtime tests for manager registration + update dispatch behavior.
3. Build integration test using sample app compile.

## Acceptance criteria
1. Generated classes include hot reload apply method and registration call.
2. Runtime manager receives metadata update callbacks and reloads matching live instances.
3. Repeated `__PopulateGeneratedObjectGraph` invocation does not duplicate children/events.
4. Sample/sourcegen projects compile with backend `SourceGen` and hot reload enabled.

## Risks and mitigations
1. Risk: repeated apply duplicates collection entries.
   - Mitigation: universal clear helper + clear-before-add emission.
2. Risk: repeated event subscriptions multiply callbacks.
   - Mitigation: `-=` before `+=` emission pattern.
3. Risk: stale instances causing leaks.
   - Mitigation: weak-reference registry cleanup on update/register.

## Delivered artifacts
1. Runtime hot reload manager APIs.
2. Generator/emitter hot-reload code emission.
3. Updated docs in `plan/`.
4. Tests and build validation.
