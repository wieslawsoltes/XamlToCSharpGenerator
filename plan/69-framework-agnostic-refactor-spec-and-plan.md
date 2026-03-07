# Framework-Agnostic Refactor Spec and Incremental Plan

Date: 2026-02-21  
Status: Proposed (execution-ready)  
Scope: Whole `XamlToCSharpGenerator` repository architecture and package boundaries

## 1. Goal

Refactor the compiler pipeline so the majority of logic is reusable across UI frameworks, while preserving current Avalonia behavior and shipping in small, low-risk steps.

This plan keeps current Avalonia users unblocked and gradually moves framework-specific semantics behind explicit extension points.

## 2. Current-State Analysis (Coupling Inventory)

## 2.1 Generator host is framework-bound
- `src/XamlToCSharpGenerator.Generator/AvaloniaXamlSourceGenerator.cs:21` hard-codes generator type and Avalonia metadata assumptions.
- `src/XamlToCSharpGenerator.Generator/AvaloniaXamlSourceGenerator.cs:276` directly creates `AvaloniaSemanticBinder`.
- `src/XamlToCSharpGenerator.Generator/AvaloniaXamlSourceGenerator.cs:296` directly creates `AvaloniaCodeEmitter`.
- `SourceItemGroup` filter assumes `AvaloniaXaml`.

Impact: No clean way to plug a different framework backend without forking generator host logic.

## 2.2 Core model includes Avalonia semantics
- `src/XamlToCSharpGenerator.Core/Models/ResolvedPropertyAssignment.cs:6` includes `AvaloniaPropertyOwnerTypeName`.
- `src/XamlToCSharpGenerator.Core/Models/ResolvedPropertyAssignment.cs:10` includes `BindingPriorityExpression`.
- `src/XamlToCSharpGenerator.Core/Models/ResolvedSetterDefinition.cs:9` includes Avalonia-specific property fields.
- `src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs:599`/`:638`/`:748` parse style/theme/include concepts in the shared parser.

Impact: Core types cannot be reused by non-Avalonia backends without carrying Avalonia-only concepts.

## 2.3 Build integration is Avalonia-only
- `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props` exposes only `AvaloniaSourceGen*` properties.
- `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets` injects only `AvaloniaXaml` item flows and disables Avalonia XamlIl tasks directly.

Impact: Build package is not backend-neutral and prevents clean multi-framework packaging.

## 2.4 Runtime layer mixes generic/runtime bridge + Avalonia policy
- Runtime includes Avalonia-specific loader/bootstrap types and service providers.
- Reflection-heavy paths still exist in hot reload/runtime bridge and event binding helpers:
  - `src/XamlToCSharpGenerator.Runtime/SourceGenRuntimeXamlCompiler.cs`
  - `src/XamlToCSharpGenerator.Runtime/SourceGenRuntimeXamlLoaderBridge.cs`
  - `src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotReloadStateTracker.cs`
  - `src/XamlToCSharpGenerator.Runtime/SourceGenEventBindingRuntime.cs`

Impact: Runtime cannot be reused as a portable base; AOT pressure remains in shared surface.

## 2.5 Solution/packaging drift
- `XamlToCSharpGenerator.slnx` references `src/XamlToCSharpGenerator.LanguageServer/...` but directory is absent.

Impact: Solution topology is inconsistent and blocks clean incremental architecture migration.

## 3. Target Architecture (End State)

## 3.1 Package topology
- `XamlToCSharpGenerator.Core`  
  Pure XAML language model, parser primitives, diagnostics, graph composition abstractions.
- `XamlToCSharpGenerator.Compiler` (new)  
  Framework-agnostic incremental host orchestration and pipeline staging.
- `XamlToCSharpGenerator.Framework.Abstractions` (new)  
  Extension contracts for framework semantics, transforms, emit policy, runtime hooks.
- `XamlToCSharpGenerator.Framework.Avalonia` (rename from current Avalonia package or additive wrapper)  
  Avalonia-specific binder/emitter/transforms/runtime adapters.
- `XamlToCSharpGenerator.Build`  
  Backend-neutral props/targets + compatibility aliases for existing Avalonia property names.
- `XamlToCSharpGenerator.Runtime.Core` (new)  
  Generic runtime registry, generated artifact dispatch contracts, diagnostics/events.
- `XamlToCSharpGenerator.Runtime.Avalonia`  
  Avalonia loader/bootstrap/hot reload integration.

## 3.2 Compiler layering contract
1. Discovery + parse (framework-neutral).
2. Framework semantic binding via profile/plugin.
3. Cross-file graph/linking via core graph service + framework callbacks.
4. Emit through framework emitter contract.
5. Runtime registration through framework runtime adapter contract.

## 3.3 Non-negotiables
- No reflection in generated code.
- Shared compiler stages must not depend on Avalonia assemblies.
- All framework behavior must be behind abstractions and integration packages.
- Existing Avalonia SourceGen behavior remains default and passes current tests at each migration step.

## 4. Incremental Refactor Strategy

Principle: Add new seams first, switch call sites second, move code last.  
No broad rename/move first; each wave must be independently reversible.

## 5. Detailed Wave Plan

## Wave 0 - Baseline and Safety Net
Objective: Freeze behavior before structural change.

Tasks:
1. Add architecture baseline report generator script (counts by project: `Avalonia` symbol references, reflection usage, pipeline stage durations).
2. Add golden snapshots for generator outputs on representative sample set.
3. Add differential tests that run "current host path" vs "new host path (disabled initially)" and assert identical outputs/diagnostics.
4. Add CI job artifacts for generated source diff summaries.

Exit criteria:
- Baseline report committed.
- Snapshot tests stable on CI.
- No behavioral changes.

## Wave 1 - Framework Profile Contracts
Objective: Introduce extension contracts without changing behavior.

Tasks:
1. Add `Framework.Abstractions` project with:
   - `IXamlFrameworkProfile`
   - `IXamlFrameworkSemanticBinder`
   - `IXamlFrameworkEmitter`
   - `IXamlFrameworkTransformProvider`
   - `IXamlFrameworkBuildContract`
2. Create Avalonia profile adapter implementing these interfaces by wrapping existing binder/emitter.
3. Update generator host internals to consume contracts, but instantiate Avalonia profile only.
4. Keep existing class names and public API unchanged.

Exit criteria:
- Host compiles through abstractions.
- Output parity with baseline snapshots.
- No user-facing property/target changes.

## Wave 2 - Generator Host Generalization
Objective: Make incremental generator backend-selectable while preserving Avalonia default.

Tasks:
1. Add `XamlToCSharpGenerator.Compiler` host orchestration project.
2. Move pipeline stage orchestration out of `AvaloniaXamlSourceGenerator` into compiler host service.
3. Keep thin `AvaloniaXamlSourceGenerator` wrapper that registers Avalonia profile with host.
4. Add generic generator entrypoint (internal first), keep only Avalonia public `[Generator]` for now.
5. Normalize hint-name strategy to include framework profile ID to avoid collisions.

Exit criteria:
- Existing tests pass.
- No duplicate hint-name regressions under `dotnet watch`.
- Host stage code is framework-neutral assembly.

## Wave 3 - Core Model Normalization
Objective: Remove Avalonia-specific members from shared core model.

Tasks:
1. Introduce framework-neutral value/property primitives in `Core.Models`.
2. Move Avalonia-only members (e.g., property field owner names, binding priority expression) into `FrameworkPayload` records keyed by semantic type.
3. Replace direct Avalonia fields in `Resolved*` models with:
   - neutral canonical value
   - optional framework extension payload
4. Update Avalonia binder/emitter to read payload extensions.
5. Keep temporary compatibility mapping layer until all call sites migrated.

Exit criteria:
- `Core` builds without Avalonia-specific model fields.
- Parser and core tests unchanged in behavior.
- Avalonia binder/emitter parity retained.

## Wave 4 - Parser Decomposition
Objective: Split universal parser from framework feature extraction.

Tasks:
1. Extract `SimpleXamlDocumentParser` into:
   - neutral XML/XAML surface parser
   - framework feature extractors (styles/themes/includes, etc.)
2. Introduce parser extension pipeline:
   - base parse tree
   - framework document enrichers
3. Move Avalonia feature extraction into `Framework.Avalonia`.
4. Keep core parser default behavior via adapter to avoid sample breaks.

Exit criteria:
- Core parser handles syntax and common directives only.
- Avalonia enrichers add same feature data as before.
- Parser snapshot tests show no net regressions for Avalonia scenarios.

## Wave 5 - Build Contract Neutralization
Objective: Decouple build package from Avalonia naming while preserving backward compatibility.

Tasks:
1. Introduce neutral properties:
   - `XamlSourceGenBackend`
   - `XamlSourceGenEnabled`
   - `XamlSourceGenInputItemGroup`
2. Keep existing `Avalonia*` properties as aliases (mapped in props/targets).
3. Introduce neutral AdditionalFiles projection target that can consume any item group.
4. Keep Avalonia defaults (`AvaloniaXaml`) unless overridden.
5. Add tests for duplicate AdditionalFiles prevention across watch/IDE.

Exit criteria:
- Existing Avalonia sample projects build unchanged.
- Neutral properties work in new sample.
- No duplicate source file warnings in watch loop for migrated sample.

## Wave 6 - Runtime Split (Core vs Avalonia)
Objective: Separate generic runtime registry from Avalonia runtime loader integration.

Tasks:
1. Create `Runtime.Core` with:
   - artifact registry
   - URI mapping contracts
   - hot reload event bus contracts
2. Move Avalonia loader/bridge/bootstrap types into `Runtime.Avalonia`.
3. Replace direct Avalonia runtime references in shared runtime logic with adapter interfaces.
4. Create explicit AOT-safe path for runtime bridge registration.

Exit criteria:
- Runtime core has zero Avalonia references.
- Avalonia runtime package composes and passes integration tests.
- Startup path parity in all sample apps.

## Wave 7 - Reflection Elimination Program
Objective: Remove reflection from shared paths and generated-path dependencies.

Tasks:
1. Reflection inventory per file with replacement design:
   - generated delegates
   - source-generated accessors
   - typed adapter interfaces
2. Prioritize hot paths: event binding runtime, hot reload state, runtime loader bridge.
3. Add analyzer rule in repo to block new reflection in compiler/generator/runtime core projects.
4. Keep unavoidable reflection isolated in framework adapter edge and explicitly documented (temporary exception list, target removal date).

Exit criteria:
- Generated code uses no reflection.
- Core/compiler/runtime-core have no reflection API usage.
- Remaining reflection (if any) confined to framework adapter and tracked.

## Wave 8 - Multi-Framework Pilot
Objective: Prove reuse by implementing a minimal second framework profile (pilot).

Tasks:
1. Add "NoUI mock framework profile" test backend:
   - simple property assignment semantics
   - minimal emitter output
2. Build pilot sample proving host/parser/core reuse.
3. Ensure shared pipeline test suite runs once per profile (Avalonia + mock).

Exit criteria:
- Second profile compiles and runs tests.
- Reuse metrics improved (target: >=70% pipeline code shared excluding framework semantic logic).

## Wave 9 - Packaging and Documentation Hardening
Objective: Ship stable agnostic architecture with migration guidance.

Tasks:
1. Publish package map and compatibility matrix.
2. Document extension authoring guide for new framework profiles.
3. Clean solution references (remove missing/stale projects, including missing LanguageServer entry or restore project).
4. Add versioned migration notes for existing Avalonia users.

Exit criteria:
- Packaging/docs complete.
- CI covers all profile configurations.
- No regressions in existing samples.

## 6. Work Breakdown by Module

| Module | Current issue | Target refactor | Wave |
|---|---|---|---|
| Generator host | Direct `new AvaloniaSemanticBinder()/AvaloniaCodeEmitter` | Host + profile factory abstraction | 1-2 |
| Core models | Avalonia-specific fields in shared records | Neutral records + framework payload extensions | 3 |
| Parser | Avalonia feature extraction in core parser | Parser core + framework enrichers | 4 |
| Build props/targets | Avalonia-only property surface | Neutral contract + alias bridge | 5 |
| Runtime | Mixed generic + Avalonia loaders | Runtime.Core + Runtime.Avalonia split | 6 |
| Reflection usage | Runtime/hot reload reflective helpers | Generated/typed access paths | 7 |
| Reusability proof | No non-Avalonia profile | Minimal second profile pilot | 8 |

## 7. Compatibility and Migration Policy

1. Keep current Avalonia property names valid for at least one major release cycle.
2. Default behavior remains Avalonia backend unless explicitly overridden.
3. New neutral properties are additive first, not breaking.
4. Any file/namespace moves require type-forwarders or wrapper classes where practical.
5. Diagnostics IDs remain stable unless explicitly versioned with release notes.

## 8. Testing Strategy for Refactor Waves

Per-wave required suites:
1. Unit tests (core/parser/model invariants).
2. Generator snapshot tests (hint names, source text determinism).
3. Differential tests (old path vs new path equality).
4. Integration tests on sample apps (`SourceGenCrudSample`, `SourceGenXamlCatalogSample`, `SourceGenConventionsSample`).
5. Runtime probes (hot reload, runtime loader, template/resource application parity).

Additional requirements:
- Add "framework-agnostic contract tests" that each profile must pass.
- Add "no reflection in generated code" test by scanning generated source for reflection API calls.

## 9. Execution Order (Recommended)

1. Wave 0  
2. Wave 1  
3. Wave 2  
4. Wave 3  
5. Wave 4  
6. Wave 5  
7. Wave 6  
8. Wave 7  
9. Wave 8  
10. Wave 9

This order minimizes risk: abstraction seams first, data model second, runtime/build split later.

## 10. Success Metrics

Architecture metrics:
- `Core` has zero direct Avalonia type references.
- `Compiler` has zero direct Avalonia type references.
- `Runtime.Core` has zero direct Avalonia type references.

Reuse metrics:
- At least 70% of pipeline code shared by two profiles.
- New profile can be added with no edits in core compiler host assembly.

Quality metrics:
- No regression in existing snapshot/differential tests.
- No duplicate hint-name failures under watch/IDE hot reload loops.
- No increase in critical diagnostics or runtime startup failures on sample apps.

## 11. Risks and Mitigations

Risk: model split causes broad churn.  
Mitigation: compatibility adapters and staged migration of call sites.

Risk: build-neutral properties break existing Avalonia flows.  
Mitigation: alias bridge + integration tests on unchanged sample project files.

Risk: runtime split affects hot reload stability.  
Mitigation: keep existing Avalonia runtime path until Runtime.Core parity tests pass.

Risk: reflection removal regresses dynamic scenarios.  
Mitigation: generate typed accessors and preserve fallback only in adapter boundary during transition.

## 12. First Execution Backlog (immediately actionable)

1. Create `Framework.Abstractions` project and contracts (Wave 1).
2. Refactor generator host to depend on contracts with Avalonia adapter wiring (Wave 1/2).
3. Add differential host tests to prove no output change (Wave 0+2).
4. Draft neutral build property aliases without behavior switch (Wave 5 prep).

---

This plan is intentionally incremental and compatibility-first so current Avalonia adoption can continue while reusable compiler architecture is extracted safely.
