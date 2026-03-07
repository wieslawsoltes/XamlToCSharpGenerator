# IDE Hot Reload Support (Visual Studio + Rider): Implementation Report

Date: 2026-02-19

## Scope delivered
Implemented IDE-focused hot-reload support path for SourceGen backend, based on plan:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/41-ide-hot-reload-visual-studio-rider-analysis-and-plan.md`

## What was implemented

### 1) Build integration for IDE compile invalidation and up-to-date checks
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets`

Changes:
1. Added `XamlToCSharpGenerator_PrepareCoreCompileInputs` target:
   - projects `@(AvaloniaXaml)` into `@(CustomAdditionalCompileInputs)` (deduplicated).
2. Added `XamlToCSharpGenerator_CollectUpToDateCheckInputDesignTime` target:
   - projects `@(AvaloniaXaml)` into `@(UpToDateCheckInput)` (deduplicated).
3. Kept deterministic `AdditionalFiles` AXAML projection and watch-item dedupe.
4. Stopped destructive `AvaloniaXaml` item rewrite to preserve IDE metadata/visibility.

### 2) Generator option contract and IDE resilience activation
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Generator/AvaloniaXamlSourceGenerator.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`

Changes:
1. Added new compiler-visible/configurable property:
   - `AvaloniaSourceGenIdeHotReloadEnabled` (default `true`).
2. Exposed IDE context properties to analyzer config:
   - `BuildingInsideVisualStudio`
   - `BuildingByReSharper`
3. Expanded resilience session detection:
   - enabled when `DotNetWatchBuild` OR (`AvaloniaSourceGenIdeHotReloadEnabled` AND (`BuildingInsideVisualStudio` OR `BuildingByReSharper`)).

### 3) Runtime fallback mode for IDE callback irregularities
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotReloadManager.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/AppBuilderExtensions.cs`

Changes:
1. Added optional IDE polling fallback API:
   - `EnableIdePollingFallback(int intervalMs = 1000)`
   - `DisableIdePollingFallback()`
   - `TryEnableIdePollingFallbackFromEnvironment(...)`
   - `ShouldEnableIdePollingFallbackFromEnvironment()`
   - `IsIdePollingFallbackEnabled`
2. Polling fallback tracks registered view method metadata tokens and applies reload when token deltas are observed.
3. Native metadata callback auto-disables polling fallback to avoid duplicate re-apply.
4. `UseAvaloniaSourceGeneratedXaml()` and `UseAvaloniaSourceGeneratedXamlHotReload(...)` now attempt env-driven fallback enable.
5. Added explicit app-builder opt-in API:
   - `UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(...)`.

### 4) Documentation updates
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/README.md`

Changes:
1. Added `AvaloniaSourceGenIdeHotReloadEnabled` property in optional backend knobs.
2. Added IDE hot-reload section describing VS/Rider behavior and integration.
3. Documented optional fallback poller extension.

## Tests added/updated

### Runtime tests
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenHotReloadManagerTests.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/AppBuilderExtensionsTests.cs`

Coverage added:
1. Enable/disable IDE polling fallback.
2. Native callback disables polling fallback.
3. Invalid polling interval guard.
4. Environment-driven fallback detection.
5. AppBuilder extension API coverage for new fallback method.

### Generator tests
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Coverage added:
1. IDE-mode resilience fallback behavior for transient invalid AXAML edits.

### Build integration tests
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`

Coverage added:
1. `AdditionalFiles` dedupe still holds.
2. `Watch` dedupe still holds.
3. `CustomAdditionalCompileInputs` projection exists for AXAML.
4. `UpToDateCheckInput` projection exists for AXAML.

## Validation commands and results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Result: Passed (`196`), Failed (`0`), Skipped (`1`).
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Result: Build succeeded (`0` errors, `0` warnings).

## Notes
1. IDE hot reload behavior still depends on each IDE's own delta-application rules; this implementation provides required project-system inputs and robust runtime refresh/fallback handling on the SourceGen side.
2. Polling fallback remains optional and auto-disables once native metadata callback path is confirmed.
