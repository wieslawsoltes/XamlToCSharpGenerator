# Wave 8 - Multi-Framework Pilot Implementation Report

Date: 2026-02-22  
Status: Completed

## Scope Executed

Wave 8 goals from `plan/69-framework-agnostic-refactor-spec-and-plan.md`:

1. Add a minimal second framework profile (`NoUi`).
2. Add a pilot sample to prove host/parser/core reuse.
3. Run shared pipeline tests per profile (Avalonia + NoUi).

## Implemented Changes

## 1) Second framework profile (`NoUi`)

Added new project:

- `src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj`

Added framework profile and contracts wiring:

- `src/XamlToCSharpGenerator.NoUi/Framework/NoUiFrameworkProfile.cs`
  - `IXamlFrameworkProfile` implementation
  - `NoUiXaml` source item group contract
  - minimal parser settings (`x:` prefix + implicit `urn:noui`)
  - minimal transform provider (empty transform config)

Added semantic binder:

- `src/XamlToCSharpGenerator.NoUi/Binding/NoUiSemanticBinder.cs`
  - maps parsed `XamlObjectNode` -> `ResolvedObjectNode`
  - minimal type resolution (`clr-namespace:`, `using:`, fallback by type name)
  - emits `AXSG0100` warning on unresolved type fallback

Added emitter:

- `src/XamlToCSharpGenerator.NoUi/Emission/NoUiCodeEmitter.cs`
  - generates deterministic source/hint names
  - emits `InitializeComponent` + `BuildNoUiObjectGraph` for class-backed files
  - emits classless artifact for non-`x:Class` files
  - captures simple property assignments and child graph materialization

Added NoUi runtime graph model:

- `src/XamlToCSharpGenerator.NoUi/NoUiObjectGraph.cs`

Added profile ID:

- `src/XamlToCSharpGenerator.Core/Models/FrameworkProfileIds.cs`
  - `NoUi`

Added generator wrapper:

- `src/XamlToCSharpGenerator.Generator/NoUiXamlSourceGenerator.cs`

Updated packaging and analyzer dependency graph:

- `src/XamlToCSharpGenerator.Generator/XamlToCSharpGenerator.Generator.csproj`
- `src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj`

## 2) Shared pipeline tests per profile

Added test harness:

- `tests/XamlToCSharpGenerator.Tests/Generator/FrameworkGeneratorTestHarness.cs`

Added NoUi profile contract tests:

- `tests/XamlToCSharpGenerator.Tests/Generator/NoUiFrameworkProfileTests.cs`

Added shared host/pipeline smoke tests (Avalonia + NoUi):

- `tests/XamlToCSharpGenerator.Tests/Generator/FrameworkPipelineProfileTests.cs`

Updated test project references:

- `tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`

## 3) Multi-framework pilot sample

Added sample:

- `samples/NoUiFrameworkPilotSample/NoUiFrameworkPilotSample.csproj`
- `samples/NoUiFrameworkPilotSample/MainView.xaml`
- `samples/NoUiFrameworkPilotSample/MainView.cs`
- `samples/NoUiFrameworkPilotSample/Controls.cs`
- `samples/NoUiFrameworkPilotSample/Program.cs`

Sample uses:

- `AdditionalFiles` with `SourceItemGroup=NoUiXaml`
- compiler-visible metadata/property wiring for generator options
- analyzer wiring to the same shared compiler host pipeline

## 4) Solution wiring

Added projects to solution:

- `src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj`
- `samples/NoUiFrameworkPilotSample/NoUiFrameworkPilotSample.csproj`

## Validation Evidence

Executed successfully:

1. `dotnet build src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj -c Debug`
2. `dotnet build src/XamlToCSharpGenerator.Generator/XamlToCSharpGenerator.Generator.csproj -c Debug`
3. `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Debug --filter "FullyQualifiedName~NoUiFrameworkProfileTests|FullyQualifiedName~FrameworkPipelineProfileTests|FullyQualifiedName~AvaloniaFrameworkProfileTests"`
4. `dotnet build samples/NoUiFrameworkPilotSample/NoUiFrameworkPilotSample.csproj -c Debug`
5. `dotnet run --project samples/NoUiFrameworkPilotSample/NoUiFrameworkPilotSample.csproj -c Debug`
6. `dotnet build src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj -c Debug`

Runtime sample output:

- `NoUI graph root: global::NoUiFrameworkPilotSample.Controls.Page`
- `Total nodes: 4`

## Exit Criteria Check

- Minimal second profile compiles: Yes.
- Pilot sample proves host/parser/core reuse: Yes.
- Shared pipeline suite executes for Avalonia + NoUi: Yes.
