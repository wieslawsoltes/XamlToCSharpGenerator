# Architecture and Components

## 1. Project Topology
1. `XamlToCSharpGenerator.Core`
   - AST/model contracts, parser contracts, diagnostic model, generator option model.
2. `XamlToCSharpGenerator.Avalonia`
   - Avalonia-specific semantic binding and emission passes.
3. `XamlToCSharpGenerator.Generator`
   - Roslyn `IIncrementalGenerator` orchestration and diagnostics reporting.
4. `XamlToCSharpGenerator.Runtime`
   - Registry and app bootstrap extension API (`UseAvaloniaSourceGeneratedXaml`).
5. `XamlToCSharpGenerator.Build`
   - `buildTransitive` props/targets for backend switch and AdditionalFiles wiring.
6. `XamlToCSharpGenerator` (distribution package)
   - single-package consumer surface that bundles analyzer/runtime/build assets.

## 2. Dependency Graph
1. `Generator -> Core`
2. `Generator -> Avalonia`
3. `Avalonia -> Core`
4. `Runtime` is independent from generator pipeline but referenced by generated source contracts.
5. `Build` has no runtime code dependency; it only contributes MSBuild behavior.

## 3. Incremental Pipeline Stages
1. **Discovery**
   - Consume `AdditionalTexts` and filter `*.axaml/*.xaml/*.paml`.
   - Respect SourceItemGroup metadata when present.
2. **Options Binding**
   - Bind global options from `AnalyzerConfigOptions` and project assembly identity.
3. **Parse**
   - Parse XAML into immutable `XamlDocumentModel` with named elements and x:Class.
4. **Semantic Bind**
   - Resolve named element types against Roslyn compilation.
   - Produce fallback diagnostics for unresolved symbols.
5. **Emit**
   - Emit deterministic partial class source with:
     - fields for named elements,
     - generated `InitializeComponent`,
     - module initializer registration into runtime registry.
6. **Diagnostics**
   - Convert parser/binder/emitter diagnostics into Roslyn diagnostics.

## 4. Runtime Wiring Design
1. Generated source registers URI factory delegates into `XamlSourceGenRegistry`.
2. Runtime exposes:
   - `UseAvaloniaSourceGeneratedXaml(this AppBuilder)` for bootstrap contract stability.
   - `AvaloniaSourceGeneratedXamlLoader.TryLoad(IServiceProvider?, Uri, out object?)` for host integration.
3. Runtime path intentionally avoids reflection and IL rewriting.

## 5. Build Integration Design
1. Props define backend and feature properties.
2. Targets inject `@(AvaloniaXaml)` into `AdditionalFiles` for the generator.
3. When source-gen backend enabled, set `EnableAvaloniaXamlCompilation=false` to bypass existing IL compile task.

## 6. Determinism Constraints
1. Stable hint naming: `Namespace.Class.XamlSourceGen.g.cs`.
2. Stable URI generation: `avares://{AssemblyName}/{NormalizedTargetPath}`.
3. Distinct named elements resolved by name in parse stage.

## 7. Extension Points
1. `IXamlDocumentParser`
2. `IXamlSemanticBinder`
3. `IXamlCodeEmitter`
4. Additional future pass packages can replace interfaces without changing generator entrypoint.
