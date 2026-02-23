# Wave 9/5/8/7 Progress Report (2026-02-22)

This report captures the first closure slice requested for:
1. Wave 9 solution cleanup
2. Wave 5 build-contract neutralization tail
3. Wave 8 multi-framework pilot hardening
4. Wave 7 reflection-governance tightening

## Wave 9 - Solution Cleanup

Status: Completed for stale project cleanup.

Implemented:
- Removed stale/missing solution project references:
  - `samples/SourceGenConventionsSample/SourceGenConventionsSample.csproj`
  - `src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj`
  - `tests/XamlToCSharpGenerator.FluentTheme.HeadlessTests/XamlToCSharpGenerator.FluentTheme.HeadlessTests.csproj`
- Verified `dotnet build XamlToCSharpGenerator.sln` succeeds.

Outcome:
- Full solution restore/build is unblocked with no missing-project MSB3202 failures.

## Wave 5 - Build Contract Neutralization Tail

Status: Implemented for AdditionalFiles/transform-rule projection seams.

Implemented:
- Added neutral properties in build props/targets:
  - `XamlSourceGenAdditionalFilesSourceItemGroup`
  - `XamlSourceGenTransformRules`
  - `XamlSourceGenTransformRuleItemGroup`
- Added alias bridging to preserve Avalonia compatibility:
  - `AvaloniaSourceGenTransformRules` <-> `XamlSourceGenTransformRules`
- Replaced hard-coded AdditionalFiles source-item literals in targets with property-driven values.
- Unified transform-rule projection to accept neutral + legacy item types.
- Added compiler-visible properties for neutral transform/item-group knobs.

Tests added/updated:
- `BuildIntegrationTests` now verifies:
  - neutral custom AdditionalFiles source group override
  - neutral transform-rule projection and item-group override

Outcome:
- Build package keeps Avalonia defaults but now projects from neutral contract knobs without hard-coded coupling in target wiring.

## Wave 8 - Multi-Framework Pilot Hardening

Status: Implemented.

Implemented:
- Moved NoUi generator entrypoint out of Avalonia generator assembly:
  - removed `src/XamlToCSharpGenerator.Generator/NoUiXamlSourceGenerator.cs`
  - added `src/XamlToCSharpGenerator.NoUi/NoUiXamlSourceGenerator.cs` (`[Generator]`)
- Updated project graph:
  - `XamlToCSharpGenerator.NoUi` now references `XamlToCSharpGenerator.Compiler`
  - `XamlToCSharpGenerator.Generator` no longer references/packs `XamlToCSharpGenerator.NoUi`
- Updated NoUi pilot sample analyzer wiring to use NoUi analyzer path directly.
- Enabled `EnforceExtendedAnalyzerRules` in NoUi project.

Outcome:
- Eliminates cross-profile analyzer instantiation warning (`CS8032` for `NoUiXamlSourceGenerator`) during Avalonia sample builds.
- Maintains NoUi pilot build/generation path.

## Wave 7 - Reflection Program (This Slice)

Status: Completed (governance + elimination closure).

Implemented:
- Added explicit runtime reflection confinement guard test:
  - `RuntimeAvalonia_ReflectionUsage_IsConfined_To_Tracked_AllowList`
  - located in `tests/XamlToCSharpGenerator.Tests/Build/ReflectionGuardTests.cs`
- This prevents new untracked reflection usage from entering `Runtime.Avalonia` while existing exception files are burned down.

Governance closure update:
- Runtime.Avalonia allowlist is now empty and enforced by reflection-guard tests.

Closed and removed from exception allowlist on 2026-02-22:
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenRuntimeXamlLoaderBridge.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadStateTracker.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenEventBindingRuntime.cs`

Remaining Wave 7 core elimination work:
- none for the tracked Wave 7 exception list.

## Validation Executed

- `dotnet build XamlToCSharpGenerator.sln`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~BuildIntegrationTests|FullyQualifiedName~FrameworkPipelineProfileTests|FullyQualifiedName~ReflectionGuardTests"`
- `dotnet build samples/SourceGenCrudSample/SourceGenCrudSample.csproj`
- `dotnet build samples/NoUiFrameworkPilotSample/NoUiFrameworkPilotSample.csproj`

All commands passed for this slice.
