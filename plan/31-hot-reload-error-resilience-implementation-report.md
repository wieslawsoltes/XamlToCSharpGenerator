# Hot Reload Error-Resilience Implementation Report

Date: 2026-02-19

## Scope completed
Implemented hot-reload-safe source generator behavior for transient AXAML errors during watch sessions.

## Code changes
1. Generator options and build contract:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
   - Added:
     - `HotReloadErrorResilienceEnabled`
     - `DotNetWatchBuild`
     - `AvaloniaSourceGenHotReloadErrorResilienceEnabled` MSBuild property (default `true`)
     - `CompilerVisibleProperty Include="DotNetWatchBuild"`

2. Diagnostics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Diagnostics/DiagnosticCatalog.cs`
   - Added `AXSG0700` (`HotReloadFallbackUsed`, warning).

3. Generator resilience pipeline:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Generator/AvaloniaXamlSourceGenerator.cs`
   - Added:
     - last-known-good generated source cache per AXAML input identity,
     - parse result wrapper carrying original input metadata,
     - hot reload session detection (`DotNetWatchBuild` or `DOTNET_WATCH=1`),
     - fallback emission reuse on parse/bind/emit failures,
     - warning demotion for transient generator errors in resilience mode,
     - `AXSG0700` warning emission when fallback source is reused.

## Test coverage added
Updated:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

New tests:
1. `HotReload_WatchMode_Uses_Last_Good_Source_When_Xaml_Is_Temporarily_Invalid`
2. `HotReload_Resilience_Can_Be_Disabled_To_Keep_Strict_Error_Behavior`

## Validation
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `126`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded (`0` errors).

## Behavior summary
1. In watch/hot-reload sessions, transient broken AXAML no longer forces generator hard-fail behavior for that edit.
2. Last successful generated source remains active until AXAML becomes valid again.
3. Standard strict behavior is preserved when resilience is disabled or outside hot-reload/watch detection.
