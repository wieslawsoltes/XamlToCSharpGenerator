# IDE Hot Reload Support (Visual Studio + Rider): Analysis and Execution Plan

Date: 2026-02-19

## Objective
Make source-generated Avalonia AXAML hot reload work reliably from IDE debug sessions (Visual Studio and Rider), not only from `dotnet watch`.

## Baseline and observed issue
Current behavior is strong for `dotnet watch` because:
1. AXAML files are included in `@(Watch)`.
2. Generator has watch-mode resilience paths keyed off `DotNetWatchBuild`.
3. Runtime manager provides metadata update callbacks (`ClearCache`, `UpdateApplication`).

But IDE workflows still regress because:
1. SourceGen backend disables Avalonia XAML compile path and therefore also drops Avalonia’s default IDE compile-input/up-to-date wiring for `@(AvaloniaXaml)`.
2. Generator resilience currently only activates for `DotNetWatchBuild`, not IDE update loops.
3. Metadata callback fallback behavior for IDE edge cases (notably Rider callback irregularities) is not present.

## External research summary

### .NET runtime callback contract
1. `MetadataUpdateHandlerAttribute` is the official integration point for app-specific post-update refresh logic.
2. The runtime invokes registered handlers via static `ClearCache(Type[]?)` and `UpdateApplication(Type[]?)`.

Reference:
- [MetadataUpdateHandlerAttribute (Microsoft Docs)](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.metadataupdatehandlerattribute)

### Visual Studio hot reload model
1. Hot reload applies supported C# deltas to running processes.
2. App/framework logic is responsible for refreshing UI/object graph state post-delta (via metadata update handlers when needed).

References:
- [Visual Studio Hot Reload](https://learn.microsoft.com/en-us/visualstudio/debugger/hot-reload)
- [Supported C# edits for Hot Reload](https://learn.microsoft.com/en-us/visualstudio/debugger/supported-code-changes-csharp)

### Rider hot reload model
1. Rider hot reload support is centered on C# code updates.
2. Framework-specific non-C# document refresh commonly requires additional integration/fallback behavior.

Reference:
- [JetBrains Rider Hot Reload](https://www.jetbrains.com/help/rider/Hot_Reload.html)

### Avalonia prior art
1. Avalonia’s own targets wire `@(AvaloniaXaml)` into both:
   - `@(CustomAdditionalCompileInputs)` (compile invalidation)
   - `@(UpToDateCheckInput)` (IDE fast up-to-date integration)
2. `Avalonia.Markup.Declarative` includes a Rider fallback watcher for native callback edge cases.

References:
- `/Users/wieslawsoltes/GitHub/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets`
- `/Users/wieslawsoltes/GitHub/Avalonia.Markup.Declarative/src/Avalonia.Markup.Declarative/HotReloadManager.cs`

## Gap analysis vs current repository

### Build integration gaps
1. `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets` currently rewrites AdditionalFiles but does not restore Avalonia-equivalent IDE compile-input/up-to-date hooks when SourceGen backend is selected.
2. Existing AXAML watch handling is focused on `dotnet watch`; IDE compile invalidation is under-specified.

### Generator gaps
1. `src/XamlToCSharpGenerator.Generator/AvaloniaXamlSourceGenerator.cs` enables hot-reload error resilience only when `DotNetWatchBuild == true`.
2. IDE-driven transient AXAML edits can still fail hard and break hot-reload update loops.

### Runtime gaps
1. `src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotReloadManager.cs` implements metadata callbacks but has no IDE fallback mode for environments where callbacks are delayed/missed.

## Design decisions

## 1) Restore IDE compile invalidation and up-to-date integration
When backend is `SourceGen`, add targets mirroring Avalonia’s necessary IDE hooks:
1. Add `@(AvaloniaXaml)` to `@(CustomAdditionalCompileInputs)` before `CoreCompile`.
2. Add `@(AvaloniaXaml)` to `@(UpToDateCheckInput)` before `CollectUpToDateCheckInputDesignTime`.

Expected effect:
- Visual Studio/Rider project systems treat AXAML edits as compile-relevant inputs, enabling proper hot-reload delta generation paths.

## 2) Expand resilience session detection for IDE updates
Extend generator option contract to detect interactive IDE update sessions (while keeping strict CI/CLI behavior):
1. Add analyzer-visible properties for `BuildingInsideVisualStudio` and `BuildingByReSharper`.
2. Add explicit override property: `AvaloniaSourceGenIdeHotReloadEnabled` (default `true`).
3. Activate resilience when:
   - `DotNetWatchBuild` OR
   - (`AvaloniaSourceGenIdeHotReloadEnabled` AND (`BuildingInsideVisualStudio` OR `BuildingByReSharper`)).

Expected effect:
- In IDE edit-debug loops, temporary invalid AXAML reuses last-good generated source and emits warning diagnostics instead of breaking updates.

## 3) Add runtime polling fallback for IDE callback irregularities
Add optional, low-overhead fallback mode in hot reload manager:
1. `EnableIdePollingFallback(int intervalMs = 1000)` / `DisableIdePollingFallback()`.
2. Poll registered view type method tokens to detect applied deltas when native callback path does not fire reliably.
3. Auto-disable polling once native `UpdateApplication` callback is observed.
4. Expose fluent app builder extension:
   - `UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(...)`.

Expected effect:
- Rider sessions with callback edge cases remain functional.

## 4) Keep AXAML project-item visibility stable
Avoid lossy metadata rewrites on `@(AvaloniaXaml)` item graph in SourceGen targets so IDE visibility (`SubType`, `Visible`, etc.) remains intact.

## Implementation plan

### Step A: Build targets and property contract
1. Update `XamlToCSharpGenerator.Build.props`:
   - add default `AvaloniaSourceGenIdeHotReloadEnabled=true`.
   - expose compiler-visible properties:
     - `BuildingInsideVisualStudio`
     - `BuildingByReSharper`
     - `AvaloniaSourceGenIdeHotReloadEnabled`
2. Update `XamlToCSharpGenerator.Build.targets`:
   - keep deterministic AdditionalFiles projection.
   - add compile-input and up-to-date targets.
   - avoid destructive rewrite of `@(AvaloniaXaml)` metadata.

### Step B: Generator resilience integration
1. Extend `GeneratorOptions` with IDE build/session flags.
2. Update resilience activation logic in `AvaloniaXamlSourceGenerator`.
3. Preserve current strict behavior for CI/CLI non-IDE non-watch builds.

### Step C: Runtime IDE fallback
1. Extend `XamlSourceGenHotReloadManager` with polling fallback API.
2. Add app-builder extension for easy opt-in.
3. Ensure polling fallback can be enabled/disabled safely and is auto-disabled when native callbacks are confirmed.

### Step D: Tests
1. Build tests (`BuildIntegrationTests`):
   - assert SourceGen backend injects `CustomAdditionalCompileInputs` and `UpToDateCheckInput` for AXAML.
2. Generator tests (`AvaloniaXamlSourceGeneratorTests`):
   - add IDE-mode resilience scenario (VS and Rider properties).
3. Runtime tests (`XamlSourceGenHotReloadManagerTests`, `AppBuilderExtensionsTests`):
   - fallback enable/disable behavior.
   - native callback disables fallback.

### Step E: Documentation
1. Add README section for IDE hot reload setup (VS/Rider).
2. Add implementation report in `plan/` with command results.

## Acceptance criteria
1. AXAML edits in Visual Studio and Rider participate in compile invalidation/up-to-date checks under SourceGen backend.
2. Generator resilience works for IDE hot-reload edit cycles, not only `dotnet watch`.
3. Runtime offers optional IDE fallback mode and remains backward-compatible.
4. Existing `dotnet watch` flow remains functional.
5. Automated test suite covers new behavior.

## Risks and mitigations
1. Risk: IDE resilience may hide real errors too aggressively.
   - Mitigation: explicit opt-out property `AvaloniaSourceGenIdeHotReloadEnabled` and clear warning diagnostics.
2. Risk: polling fallback overhead.
   - Mitigation: opt-in API and coarse polling interval default.
3. Risk: target changes reintroduce duplicate file projections.
   - Mitigation: retain deterministic AdditionalFiles dedupe and add regression assertions.
