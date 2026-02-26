# ControlCatalog Compiler Compatibility Plan (2026-02-25)

## Goal
- Build and run `samples/ControlCatalog` and platform heads with minimal project-local changes.
- Fix compiler/runtime support in generator/runtime layers first.
- Track compilation/runtime issues and close them one by one.

## Current Scope
- Project: `samples/ControlCatalog/ControlCatalog.csproj`
- App host: `samples/ControlCatalog.Desktop`
- Constraint: prefer fixes in:
  - `src/XamlToCSharpGenerator.Avalonia/*`
  - `src/XamlToCSharpGenerator.Runtime.*/*`
- Avoid broad edits in copied ControlCatalog XAML unless no framework-level fix is viable.

## Baseline Buckets
- [x] AXSG0102 literal conversion failures (baseline: 39 unique lines)
- [x] AXSG0101 unresolved property/attached property (baseline: 13 unique lines)
- [x] AXSG0300 empty style selector handling (baseline: 2 unique lines)
- [x] AXSG0305 missing BasedOn theme key behavior (baseline: 1 unique line)
- [x] AXSG0402 include/merge-group handling (baseline: 1 unique line)
- [x] Runtime crash on generated conversions (e.g., `TimeSpan.Parse("0.25")`)
- [x] Runtime loader integration parity for copied sample (`AvaloniaXamlLoader.Load(this)` path)

## Execution Log
1. [x] Created plan file.
2. [x] Rebuild and capture current diagnostics snapshot.
3. [x] Fix highest-volume bucket first, rerun rebuild, record delta.
4. [x] Iterate until compile is clean or only explicitly deferred warnings remain.
5. [x] Validate desktop run path.
6. [x] Validate other heads compile.
7. [x] Fix runtime content projection regression (window body blank with only selected header visible).

## Current Status Snapshot (Post-Fixes)
- Command: `dotnet build samples/ControlCatalog/ControlCatalog.csproj -t:Rebuild -v minimal`
- Log: `/tmp/controlcatalog_rebuild_after_patch_20260225_pass6.log`
- Result:
  - `ControlCatalog` compiles successfully.
  - `AXSG*` warnings for `ControlCatalog` are now `0`.
  - Remaining warnings are non-generator C# warnings in copied sample/support projects.
- Head builds:
  - `samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj`: builds (`/tmp/controlcatalog_desktop_build_20260225.log`), 1 nullable warning in copied Win native sample.
  - `samples/ControlCatalog.Android/ControlCatalog.Android.csproj`: blocked by missing `macos` workload (`NETSDK1147`) (`/tmp/controlcatalog_android_build_20260225.log`).
  - `samples/ControlCatalog.iOS/ControlCatalog.iOS.csproj`: blocked by missing `ios` workload (`NETSDK1147`) (`/tmp/controlcatalog_ios_build_20260225.log`).

## Issue-By-Issue Resolution (This Pass)
1. [x] Re-validate compile status across all copied ControlCatalog heads.
   - `dotnet build samples/ControlCatalog/ControlCatalog.csproj -v minimal`: success.
   - `dotnet build samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj -v minimal`: success.
   - `dotnet build samples/ControlCatalog.Android/ControlCatalog.Android.csproj -v minimal`: `NETSDK1147` workload blocker (`macos`).
   - `dotnet build samples/ControlCatalog.iOS/ControlCatalog.iOS.csproj -v minimal`: `NETSDK1147` workload blocker (`ios`).
   - `dotnet build samples/ControlCatalog.NetCore/ControlCatalog.NetCore.csproj -v minimal`: missing project file (copied folder has support code but no project).
2. [x] Runtime crash: `ToolTip.CustomPopupPlacementCallback` emitted as non-matching value for Avalonia `SetValue`.
   - Symptom: `Invalid value for Property 'CustomPopupPlacementCallback'`.
   - Root cause: delegate method-group conversion existed on CLR property assignment path, but not on Avalonia-property assignment path.
   - Fix:
     - Threaded `rootTypeSymbol` into `TryBindAvaloniaPropertyAssignment(...)`.
     - Added delegate conversion branch for Avalonia property assignment.
     - Updated delegate emission to construct the exact target delegate type.
3. [x] Runtime crash: `Could not find control 'Decorations'` on `MainView.OnAttachedToVisualTree`.
   - Root cause: generated parent object graph was overwriting existing name scopes on nested class-backed controls.
   - Fix:
     - Added generated helper `__TrySetNameScope(...)` to assign scope only when no scope exists.
     - Replaced direct `NameScope.SetNameScope(...)` emissions with `__TrySetNameScope(...)`.
4. [x] Runtime crash: `Invalid value for Property 'Easing': DeferredStaticResourceContent`.
   - Root cause: deferred static-resource values could flow through generic markup-extension wrapping into typed `SetValue` calls.
   - Fix:
     - Centralized coercion in `ProvideMarkupExtensionValue(...)` for all extension result paths.
     - In `CoerceMarkupExtensionResultForTargetProperty(...)`, resolve `IDeferredContent` eagerly when possible.
     - If unresolved deferred value targets an Avalonia property, return `AvaloniaProperty.UnsetValue` to avoid invalid typed assignment.
5. [x] Desktop runtime verification after fixes.
   - Command: `dotnet run --no-build --project samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj`
   - Result: app starts (no unhandled exception; output: `App activated: Background`).
6. [x] Runtime blank-content regression fix (`HamburgerMenu`/`TabControl` selected content).
   - Symptom: `ControlCatalog` main window showed only selected header (`Composition`) while content area stayed blank.
   - Root cause: generated top-down collection attach emitted before child initialization, so first `TabItem` could be auto-selected before its `Content` was assigned, leaving `SelectedContent` latched as `null`.
   - Fix:
     - Updated emitter top-down attach ordering in `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`.
     - Collection/property top-down attach statements are now deferred until after node property/content initialization, still before `__EndInit(...)`.
   - Verification:
     - Rebuild succeeded: `dotnet build samples/ControlCatalog/ControlCatalog.csproj -v minimal`.
     - Generated output now assigns `TabItem.Content` before `__TryAddToCollection(...Items, tabItem)`.

## Baseline Snapshot
- Command: `dotnet build samples/ControlCatalog/ControlCatalog.csproj -t:Rebuild -v minimal`
- Log: `/tmp/controlcatalog_rebuild_20260225.log`
- ControlCatalog AXSG warnings:
  - 56 unique source warning lines (112 emitted due duplicate target pass).
  - Distribution:
    - `AXSG0102`: 39 unique lines
    - `AXSG0101`: 13 unique lines
    - `AXSG0300`: 2 unique lines
    - `AXSG0305`: 1 unique line
    - `AXSG0402`: 1 unique line
- Priority order for fixes:
  1. Literal conversion infra (`AXSG0102`) for URI/image/icon/container-query/font-features/delegate references.
  2. Property/attached-property resolution parity (`AXSG0101`), especially property-element scenarios.
  3. Style/include diagnostics parity (`AXSG0300`, `AXSG0305`, `AXSG0402`) with tolerant runtime behavior where appropriate.

## Notes
- Existing local uncommitted sample imports and runtime changes are present in workspace; this plan tracks forward fixes only and avoids destructive resets.
