# AvaloniaSemanticBinder Split + Decoupling Plan

Date: 2026-02-22  
Status: Planned (execution-ready)  
Scope: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

## 1. Objective

Refactor the current monolithic binder into small, maintainable units and extract reusable (framework-agnostic) semantic services where possible, while preserving current Avalonia behavior and diagnostics.

Primary outcomes:
1. Split `AvaloniaSemanticBinder.cs` into focused files (partial class + helper services).
2. Reduce direct Avalonia coupling by introducing reusable semantic abstractions in Core/Framework contracts.
3. Keep parity with current output (diagnostics + generated code behavior).

## 2. Baseline Snapshot (Current State)

Measured on 2026-02-22:
1. File size: `15,892` lines.
2. Methods: `332` methods (including nested helper members).
3. Single class contains mixed responsibilities:
   - transform pipeline orchestration,
   - object/style/theme/include/template binding,
   - binding and markup parsing,
   - event binding,
   - conversion policy,
   - type/namespace resolution,
   - selector logic,
   - Roslyn expression rewriting,
   - internal caches and model structs.

Major line-range clusters (for split boundaries):
1. Pipeline/context/pass wiring: ~`18-1367`.
2. Object node binding core: ~`1368-3157`.
3. Styles/themes/includes/resources/templates: ~`3158-4999`.
4. Binding/event/markup handling: ~`5000-10023`.
5. Value conversion + property assignment policy: ~`10024-13150`.
6. Expression/selector/type-resolution/cache internals: ~`13151-15892`.

## 3. Refactor Constraints

1. No behavior change in Phase 1 split (mechanical move only).
2. No reflection introduction in binder, emitted code, or new shared services.
3. Preserve deterministic output and diagnostics IDs/messages.
4. Preserve hot reload resilience behavior.
5. Keep public binder entrypoint stable: `AvaloniaSemanticBinder : IXamlSemanticBinder`.

## 4. Target File Layout (Phase 1 Mechanical Split)

Keep one public type, move logic into partial files:

`/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/`
1. `AvaloniaSemanticBinder.cs`
   - class shell, constants, `Bind(...)`, high-level orchestration only.
2. `AvaloniaSemanticBinder.Pipeline.cs`
   - pass interfaces/classes, `BindingTransformContext`, pass execution.
3. `AvaloniaSemanticBinder.ObjectGraph.cs`
   - `BindObjectNode`, construction expression wiring, conditional branches.
4. `AvaloniaSemanticBinder.StylesThemes.cs`
   - `BindStyles`, `BindControlThemes`, selector target binding.
5. `AvaloniaSemanticBinder.ResourcesTemplatesIncludes.cs`
   - `BindResources`, `BindTemplates`, `BindIncludes`, template checks.
6. `AvaloniaSemanticBinder.BindingMarkup.cs`
   - binding markup parsing, compiled/runtime binding builders.
7. `AvaloniaSemanticBinder.EventBinding.cs`
   - event-binding parsing/validation/signature resolution.
8. `AvaloniaSemanticBinder.ValueConversion.cs`
   - conversion policy, property/setter conversion, coercion helpers.
9. `AvaloniaSemanticBinder.Selectors.cs`
   - selector parse/normalize/build and related structs/enums.
10. `AvaloniaSemanticBinder.TypeResolution.cs`
    - XAML type resolution, namespace fallback, alias resolution, caches.
11. `AvaloniaSemanticBinder.ExpressionSyntax.cs`
    - Roslyn syntax walkers/rewriters and expression utilities.
12. `AvaloniaSemanticBinder.Models.cs`
    - internal structs/enums/records used across partials.

## 5. Decoupling Targets (Phase 2+)

Extract reusable logic from Avalonia binder into framework-neutral services.

## 5.1 Move to Core (framework-agnostic)
1. `IXamlTypeResolutionService` + default implementation:
   - XML namespace map handling,
   - `clr-namespace` parsing,
   - directive/intrinsic resolution hooks,
   - deterministic resolution strategy metadata.
2. `IXamlValueConversionService` (neutral conversion pipeline shell):
   - literal/null/enum/primitive conversion,
   - typed conversion contract,
   - pluggable framework converters.
3. `IXamlBindingPathService`:
   - path tokenization and segment typing,
   - method/indexer segment parse.
4. `IXamlSelectorParserService` (grammar + tokenization only).
5. `IXamlMarkupSemanticService`:
   - shared markup-extension normalization and argument handling.

## 5.2 Keep Avalonia-specific adapters
1. `AvaloniaTypeResolutionProfile`:
   - Avalonia default namespaces,
   - Avalonia metadata attributes,
   - Avalonia extension suffix policy.
2. `AvaloniaValueConversionProfile`:
   - AvaloniaProperty semantics,
   - BindingPriority and Avalonia binding object rules,
   - brush/transform/resource-specific conversions.
3. `AvaloniaBindingSemanticProfile`:
   - RelativeSource modes,
   - TemplateBinding and resource extension semantics,
   - event/routed-event specifics.

## 5.3 New seam in framework abstractions
Add/extend contracts in framework abstractions:
1. `IXamlFrameworkSemanticProfile` (type, conversion, binding, selector sub-services).
2. `IXamlFrameworkDiagnosticPolicy` for strict/compat behavior toggles.

## 6. Script-First Execution Plan

Use scripts to minimize manual editing risk.

## Wave A - Inventory + Mapping Script
Deliverables:
1. `eng/scripts/avalonia-binder-inventory.sh`
   - outputs method index (`line`, `name`, `arity`, `dependency tags`).
2. `eng/scripts/avalonia-binder-coupling-report.sh`
   - reports Avalonia metadata-name usage and hot spots.
3. `eng/scripts/avalonia-binder-split-map.json`
   - mapping of methods/types to target partial files.

Exit criteria:
1. Script-generated inventory committed.
2. Split map reviewed and stable.

## Wave B - Mechanical Split Script (No behavior change)
Deliverables:
1. `eng/tools/BinderRefactorTool/` (small Roslyn rewrite utility) OR shell+dotnet-script equivalent.
2. `eng/scripts/apply-avalonia-binder-split.sh`:
   - reads split map,
   - rewrites partial files,
   - preserves trivia/comments,
   - keeps deterministic member order.

Exit criteria:
1. Binder compiles with identical behavior.
2. No output or diagnostics diffs in golden tests.

## Wave C - Core Service Extraction
Deliverables:
1. New core services (`TypeResolution`, `BindingPath`, `Selector`, neutral conversion shell).
2. Avalonia adapter/profile implementations.
3. Binder partials switched from static helper calls to injected/constructed service dependencies.

Exit criteria:
1. Avalonia binder logic reduced to orchestration + Avalonia profile rules.
2. Shared services have zero Avalonia type-string constants.

## Wave D - Contract Hardening + Cleanup
Deliverables:
1. Framework abstraction updates for semantic profile interfaces.
2. Remove dead helpers/duplicates from binder partials.
3. Update docs and architecture notes.

Exit criteria:
1. `AvaloniaSemanticBinder` files are cohesive and <2,000 LOC each target.
2. No single helper file exceeds ~1,500 LOC except temporary transition files.

## 7. Validation and Parity Gates

Required gates per wave:
1. Build: full solution build.
2. Tests:
   - binder/unit tests,
   - generator snapshot tests,
   - selected runtime/headless parity tests.
3. Differential checks:
   - same diagnostics (ID + location),
   - same generated source fingerprints for representative fixtures,
   - same runtime behavior probes for styles/templates/resources/bindings.
4. Performance:
   - binder execution time must not regress >5% on baseline fixture set.

## 8. Commit Strategy (Granular)

1. Commit 1: inventory scripts + split map only.
2. Commit 2: mechanical split output only (no semantic changes).
3. Commit 3..N: each extracted core service and corresponding binder call-site migration.
4. Final commit: cleanup, docs, and deprecation/removal of obsolete helpers.

## 9. Risks and Mitigations

1. Risk: accidental behavior drift during file moves.
   - Mitigation: Wave B is strictly mechanical and diff-gated.
2. Risk: over-generalizing too early breaks Avalonia parity.
   - Mitigation: extract neutral services only when no Avalonia-only semantics are required; keep profile adapters explicit.
3. Risk: merge conflict churn due large file touch.
   - Mitigation: script-based deterministic member ordering + granular commits.

## 10. Definition of Done

1. `AvaloniaSemanticBinder.cs` is split into focused partial files with clear responsibility boundaries.
2. At least type resolution, binding-path parsing, selector grammar, and neutral conversion shell are reusable without Avalonia references.
3. Avalonia-specific rules live in profile/adapter classes.
4. All existing parity tests pass, and no new reflection paths are introduced.
