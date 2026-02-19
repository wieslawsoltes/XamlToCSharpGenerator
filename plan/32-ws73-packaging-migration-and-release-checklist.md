# WS7.3 Packaging, Migration, and Release Checklist

Date: 2026-02-19

## Objective
Close remaining packaging and migration closure work for SourceGen backend release readiness.

## Package Topology
1. `XamlToCSharpGenerator` (single-package consumer entrypoint)
   - bundles analyzer assemblies:
     - `XamlToCSharpGenerator.Generator.dll`
     - `XamlToCSharpGenerator.Core.dll`
     - `XamlToCSharpGenerator.Avalonia.dll`
   - bundles runtime assemblies:
     - `lib/net10.0/XamlToCSharpGenerator.Runtime.dll`
   - bundles build-transitive assets:
     - `buildTransitive/XamlToCSharpGenerator.props`
     - `buildTransitive/XamlToCSharpGenerator.targets`
2. `XamlToCSharpGenerator.Build` (direct build-transitive package)
3. `XamlToCSharpGenerator.Runtime` (runtime-only package)
4. `XamlToCSharpGenerator.Generator` (analyzer-only package)

## Consumer Migration Steps
1. Add package reference:
   ```xml
   <PackageReference Include="XamlToCSharpGenerator" Version="<version>" />
   ```
2. Opt-in backend:
   ```xml
   <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
   ```
3. Add runtime bootstrap:
   ```csharp
   AppBuilder.Configure<App>()
       .UsePlatformDetect()
       .UseAvaloniaSourceGeneratedXaml();
   ```
4. Optional strict/diagnostic switches:
   - `AvaloniaSourceGenStrictMode`
   - `AvaloniaSourceGenCreateSourceInfo`
   - `AvaloniaSourceGenUseCompiledBindingsByDefault`
5. Validate diagnostics and fix `AXSG####` findings.
6. Roll back instantly if needed by switching backend to `XamlIl`.

## Compatibility and Support Policy
1. v1 target: C# Avalonia applications.
2. Non-C# projects remain on `XamlIl`.
3. No dynamic runtime XAML compiler API in v1 SourceGen backend.
4. SourceGen backend is opt-in and does not change default Avalonia behavior.

## Release Validation Checklist
1. Package pack validation:
   - `dotnet pack /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj -c Release`
2. Package content validation:
   - analyzer DLLs present under `analyzers/dotnet/cs`.
   - runtime DLL present under `lib/net10.0`.
   - build-transitive props/targets present and named as consumer imports expect.
3. Build integration validation:
   - backend switch enables SourceGen compiler path.
   - Avalonia XAML compile task is disabled in SourceGen backend mode.
4. Sample validation:
   - `SourceGenCrudSample` build succeeds with `AvaloniaXamlCompilerBackend=SourceGen`.
5. Differential validation:
   - dual-backend fixture baseline (`XamlIl` + `SourceGen`) succeeds.

## Implemented in This Wave
1. Added package integration test:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PackageIntegrationTests.cs`
2. Added baseline dual-backend fixture:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialBackendTests.cs`
3. Updated top-level README migration and compatibility guidance.

## Remaining Post-Closure Follow-ups
1. Promote perf harness from opt-in lane to enforced threshold lane.
2. Expand differential baseline into feature-tagged runtime behavior corpus.
3. Eliminate non-fatal startup duplicate-source warnings observed in `dotnet watch`.

## Release Warning Policy (Current)
1. Parity-blocking:
   - backend-switch failures,
   - generator crashes (`CS8785`/duplicate hint names),
   - differential fixture semantic/runtime mismatches.
2. Release-hardening (non-parity semantics):
   - `NU1903` sample transitive vulnerability warnings (current: present; requires dependency upgrade task).
   - `CS1591`/`RS2008`/`RS1036` warning debt in analyzer/runtime projects (current: present; fix or explicit acceptance required before final package promotion).

## Follow-up Status Update
1. Perf harness is now environment-gated and wired to dedicated CI lane:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerformanceHarnessTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerfFactAttribute.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/.github/workflows/ci.yml`
2. Differential baseline is expanded to feature-tagged corpus:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialFeatureCorpusTests.cs`
3. Duplicate AXAML watch warnings are covered by regression:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`
