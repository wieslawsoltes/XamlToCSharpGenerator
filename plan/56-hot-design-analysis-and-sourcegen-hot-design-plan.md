# Hot Design Analysis and SourceGen Hot Design Plan

## Scope
Design and implement a SourceGen-native hot design mode for Avalonia that:
1. can be toggled at runtime,
2. can accept runtime edit commands (tool-invocable),
3. propagates edits back to source files,
4. coordinates with existing hot reload pipeline,
5. stays extensible for custom transports/appliers.

## Architecture Findings

### 1) Dev-server mediated runtime editing API
Runtime APIs should support file edits and optional waiting for full hot reload completion:
- request/response operation model,
- correlation-based waiting.

### 2) Metadata update + UI refresh pipeline separation
Keep these concerns separate:
- delta application + metadata callbacks,
- visual-tree update orchestration on UI thread,
- extensible element handlers for state capture/restore.

### 3) Status tracking and operation correlation
Track source (`Runtime`, `DevServer`, `Manual`) and operation status to keep runtime tooling reliable.

### 4) Dev server role
Treat dev server transport as an optional channel for hot reload + hot design update flow.

## Gap Analysis vs Current XamlToCSharpGenerator
Current repo already has:
1. metadata-update-based hot reload manager,
2. handler extensibility,
3. IDE polling fallback,
4. source-path watchers,
5. resilience fallback.

Missing for hot design parity:
1. runtime hot design mode toggle/state,
2. runtime command/tool surface for edit application,
3. first-class document registration (build URI + source path + type),
4. source writeback orchestration + completion waiting policy,
5. dedicated extensibility surface for custom update transports/appliers.

## SourceGen Hot Design Spec

### A) Compiler plumbing
1. Add build property: `AvaloniaSourceGenHotDesignEnabled` (default `false`).
2. Add generator option and binder pass-through so emitter can generate hot design registration code.
3. Generated `InitializeComponent` must register class-backed root with runtime hot design manager using:
   - root type,
   - build URI,
   - source path,
   - runtime apply delegate.

### B) Runtime hot design manager
Add `XamlSourceGenHotDesignManager` with:
1. Mode control:
   - `Enable(...)`, `Disable()`, `Toggle()`.
2. Registration/tracking:
   - `Register(instance, applyAction, options)`.
   - weak-reference instance tracking by type/build URI.
3. Query surface:
   - `GetRegisteredDocuments()`.
4. Update surface:
   - `ApplyUpdate(...)` / `ApplyUpdateAsync(...)`.
   - resolve target by build URI or type,
   - persist edited XAML text to source file,
   - optionally wait for hot reload completion event,
   - optional runtime fallback apply.
5. Events:
   - mode changed,
   - update applied,
   - update failed.

### C) Tool-invocable runtime API
Add `XamlSourceGenHotDesignTool` static facade for dev-tool/debug-console usage:
1. `Enable/Disable/Toggle`.
2. `GetStatus` + `ListDocuments`.
3. `ApplyUpdate` by URI/type with raw XAML text.

### D) Extensibility
Add `ISourceGenHotDesignUpdateApplier` extension contract:
1. manager can register custom appliers,
2. appliers selected by priority + capability,
3. default file-system applier included.

### E) AppBuilder integration
Add extension method:
- `UseAvaloniaSourceGeneratedXamlHotDesign(...)`
that configures and enables hot design mode.

### F) Reliability safeguards
1. If source path is unavailable, return non-fatal failure result.
2. Do not crash app on edit/apply failure.
3. Keep existing hot reload behavior unchanged when hot design disabled.

## Config Surface

### MSBuild
- `AvaloniaSourceGenHotDesignEnabled` (default `false`).

### Runtime options
`SourceGenHotDesignOptions`:
1. `PersistChangesToSource` (default `true`)
2. `WaitForHotReload` (default `true`)
3. `HotReloadWaitTimeout` (default `10s`)
4. `FallbackToRuntimeApplyOnTimeout` (default `false`)
5. `EnableTracing` (default `false`)

## Implementation Plan

### Wave 1
1. Add generator/build property wiring.
2. Add emitted registration call.
3. Add runtime registration models.

### Wave 2
1. Implement hot design manager + default applier.
2. Implement runtime tool facade.
3. Add AppBuilder extension.

### Wave 3
1. Add tests (generator + runtime + appbuilder).
2. Update README with usage and caveats.
3. Validate with sample build/tests.

## Acceptance Criteria
1. With `AvaloniaSourceGenHotDesignEnabled=true`, generated code contains hot design registration.
2. Runtime can list tracked documents and toggle mode.
3. Runtime API can write updated XAML text to source path.
4. Runtime API can wait for hot reload completion and report success/timeout.
5. Custom applier registration can override default behavior.
6. Existing hot reload tests stay green.
