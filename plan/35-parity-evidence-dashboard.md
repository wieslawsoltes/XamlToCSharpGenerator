# Parity Evidence Dashboard

Date: 2026-02-19

## Purpose
Map current parity capabilities to concrete automated evidence (tests/build integration) and identify remaining non-semantic release hardening items.

## Capability -> Evidence

1. Backend switch + Avalonia task bypass
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`
   - Coverage:
     - `SourceGen_Backend_Disables_Avalonia_Xaml_Compilation_And_Injects_AdditionalFiles`
     - `Default_Backend_Does_Not_Inject_SourceGen_AdditionalFiles`

2. AdditionalFiles projection dedupe + watch duplicate-source regression
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`
   - Coverage:
     - `SourceGen_Backend_Rewrites_AvaloniaXaml_AdditionalFiles_Without_Duplicates`
     - `SourceGen_Backend_DotNetWatch_List_Does_Not_Report_Duplicate_Axaml_Source_Files`
   - Build contract:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets`

3. SourceGen hot-reload resilience fallback
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
   - Coverage:
     - `HotReload_WatchMode_Uses_Last_Good_Source_When_Xaml_Is_Temporarily_Invalid`
     - `HotReload_Resilience_Can_Be_Disabled_To_Keep_Strict_Error_Behavior`

4. Baseline dual-backend differential harness
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialBackendTests.cs`
   - Coverage:
     - `Simple_Fixture_Builds_With_Both_XamlIl_And_SourceGen_Backends`

5. Feature-tagged differential corpus (build + runtime smoke markers)
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialFeatureCorpusTests.cs`
   - Coverage categories:
     - `bindings`
     - `styles`
     - `templates`
     - `resources` (include/static-resource path)

6. Package content and migration closure
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PackageIntegrationTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/README.md`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/32-ws73-packaging-migration-and-release-checklist.md`

7. Performance baseline + threshold gating lane
   - Test harness:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerformanceHarnessTests.cs`
   - Environment-gated attribute:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerfFactAttribute.cs`
   - CI lane:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/.github/workflows/ci.yml` (`perf-sourcegen` job)

8. Routed-event transform parity (`FooEvent` + `AddHandler` path)
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
   - Coverage:
     - `Reports_Diagnostic_For_Incompatible_Clr_Event_Handler_Signature`
     - `Generates_Routed_Event_Field_Subscription_Using_AddHandler`
     - `Reports_Diagnostic_For_Incompatible_Routed_Event_Handler_Signature`
     - `Reports_Diagnostic_For_Invalid_Routed_Event_Field_Definition`

9. Source-info event identity emission
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
   - Coverage:
     - `Emits_Event_Source_Info_Registrations_When_Enabled`

## Remaining Non-Semantic Hardening

1. Sample transitive dependency vulnerability warning:
   - `NU1903` from `SkiaSharp 2.88.3` in sample graph.
   - This is a release-hardening dependency update item, not a SourceGen parity semantics failure.

2. Public API documentation/analyzer warning debt:
   - `CS1591`, `RS2008`, `RS1036` warning set in current solution build.
   - These warnings are quality/compliance items and should be either fixed or explicitly accepted in release criteria.
