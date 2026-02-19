# Remaining Feature-Complete Task Breakdown (Detailed)

This document expands the remaining backlog from `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/23-feature-complete-remaining-master-plan.md` after completed Wave 1, Wave 2, Wave 3A, and Wave 3B work in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/24-feature-complete-execution-tracker.md`.

## 1. Current Baseline
1. `WS1` foundation complete: class-backed and classless generation paths compile and register URI artifacts.
2. `WS2.B1` complete: compiled-binding stream operator (`^`) parity for `Task`/`IObservable` paths is implemented.
3. `WS2.B2` complete: query source conflict diagnostics and typed `$parent[type;level]` normalization are implemented.
4. `WS2.B3` mostly complete: `Source={x:Reference ...}` binding-source normalization and binding alias additions are implemented.
5. `WS2.B4` complete: generalized `x:Reference` object materialization is implemented with runtime helper-based namescope resolution.
6. `WS3.1` advanced slice complete: selector pseudo-functions/property predicates plus inherited-type nested selector fallback and nested `:not(..., ...)` branch lowering are implemented.
7. `WS3.2` partial complete: default style/template binding-priority emission and typed setter-value conversion are implemented.
8. `WS4.1` partial complete: deferred template-content factory emission and content-property-aware attachment (`[Content]` parity slice) are implemented.
9. Build/test baseline is green (`88` tests passing).

## 2. Remaining Detailed Backlog

### WS3: Style/Selector/Setter/ControlTheme Runtime Equivalence

#### Task Group WS3.1: Selector Grammar Coverage
1. Finish escaped selector token parity and parser-equivalent invalid-selector diagnostics for unsupported grammar.
2. Close remaining selector forms from transformer matrix not yet lowered in `TryBuildSimpleSelectorExpression(...)`.
3. Add strict invalid-selector test corpus with location-mapping assertions (`AXSG0300`).

Acceptance:
1. Selector corpus compiles with expected expression output.
2. Invalid selectors produce `AXSG0300` with segment-level locations.

#### Task Group WS3.2: Setter Value/Precedence Parity
1. Extend precedence parity beyond binding initializer defaults (for example, control-template property setter priority overload paths).
2. Reconcile duplicate setter behavior with Avalonia precedence/runtime checker rules.
3. Expand setter-value conversion parity to remaining markup/value edge cases validated against fixture differentials.

Acceptance:
1. Style/theme setter semantics match XamlIl baseline fixtures.
2. Duplicate/invalid setter diagnostics (`AXSG0301/AXSG0304`) are deterministic.

#### Task Group WS3.3: ControlTheme Materialization
1. Move from metadata-only control theme registration to materialized generated theme construction path.
2. Implement `BasedOn` chain resolution and validation.
3. Validate `ThemeVariant` behavior and fallback.

Acceptance:
1. ControlTheme fixtures resolve runtime target/theme behavior equivalently.
2. Invalid target types and setters emit `AXSG0302/AXSG0303` with file mapping.

### WS4: Templates and Deferred Content

#### Task Group WS4.1: Deferred Content IR
1. Formalize deferred-content node model in core IR (current slice is emitter-level).
2. Add explicit lifecycle hooks/identity for generated template factories to support richer runtime services.
3. Complete nested template namescope and metadata parity scenarios with differential fixtures.

Acceptance:
1. Template generation avoids eager tree instantiation for deferred scenarios.
2. Generated code remains deterministic across builds.

#### Task Group WS4.2: Template Family Completion
1. Complete `ControlTemplate`, `ItemsPanelTemplate`, `TreeDataTemplate` behavior parity.
2. Add `TargetType`/`DataType` checker equivalents for each template kind.
3. Expand binder tests for template-specific diagnostics.

Acceptance:
1. Template fixtures pass with equivalent object graph/runtime behavior.
2. Template diagnostics (`AXSG0500/AXSG0501`) match expected conditions.

### WS5: Resources, Includes, Group Transform Materialization

#### Task Group WS5.1: Include Graph Construction
1. Build cross-file include graph in incremental pipeline stage.
2. Materialize include order and merge target behavior (`MergedDictionaries`, `Styles`).
3. Add cycle detection and deterministic diagnostic output.

Acceptance:
1. Multi-file include fixtures resolve in deterministic order.
2. Include diagnostics (`AXSG0400/AXSG0401/AXSG0402`) include explicit source + target context.

#### Task Group WS5.2: Resource Resolution Parity
1. Implement merged dictionary precedence parity.
2. Add static/dynamic resource lookup consistency checks against baseline.
3. Add resource key collision handling parity.

Acceptance:
1. Resource lookup behavior matches XamlIl fixture expectations.
2. No unsupported fallback path for supported merge/include scenarios.

### WS6: NameScope, SourceInfo, Runtime Services

#### Task Group WS6.1: NameScope Completion
1. Extend namescope registration to nested/template scopes.
2. Ensure `FindControl` parity for templated/nested names.
3. Add hot-reload safe namescope refresh behavior.

Acceptance:
1. Nested/template namescope tests pass in runtime and hot-reload scenarios.
2. No duplicate registration leaks across hot reload applies.

#### Task Group WS6.2: SourceInfo Mapping Parity
1. Expand source-info registration granularity for template/style/setter/binding nodes.
2. Preserve stable node identity keys across incremental runs.
3. Add source-info differential tests against baseline location mapping.

Acceptance:
1. Source-info queries map to expected file/line/column for all supported node families.
2. Devtools/hot reload metadata paths resolve correctly.

#### Task Group WS6.3: Runtime Service Dispatch
1. Harden URI loader registry behavior for class-backed + classless documents.
2. Add conflict handling for duplicate URI registrations.
3. Add runtime fallback diagnostics for missing registrations.

Acceptance:
1. URI resolution tests pass for window/control/resource dictionary artifacts.
2. Hot reload manager can reapply generated graph without loader drift.

### WS7: Build Integration, Performance, Release Hardening

#### Task Group WS7.1: Differential Harness
1. Add fixture runner that compiles each case with `XamlIl` and `SourceGen` backends.
2. Capture generated C# snapshots and runtime behavior assertions.
3. Add per-feature parity dashboard output in CI logs.

Acceptance:
1. Mandatory parity rows in `/plan/04-parity-matrix.md` are green.
2. Regressions fail CI with feature-tagged output.

#### Task Group WS7.2: Determinism and Incremental Performance
1. Add deterministic output tests across path casing and machine-local roots.
2. Add incremental edit benchmark harness for single-file and cross-file edits.
3. Enforce regression thresholds for full build and incremental rebuild.

Acceptance:
1. Determinism tests pass across repeated runs.
2. Perf budgets are documented and enforced in CI.

#### Task Group WS7.3: Packaging and Migration
1. Finalize standalone package metadata and build-transitive wiring.
2. Write migration guide with backend switch examples and compatibility caveats.
3. Publish API/diagnostics contract table and versioning guarantees.

Acceptance:
1. NuGet package works in clean external sample app.
2. Migration doc enables opt-in switch without manual patching.

## 3. Suggested Execution Order (Current)
1. Wave 4: WS3.1 + WS3.2 + WS3.3.
2. Wave 5: WS4.1 + WS4.2.
3. Wave 6: WS5.1 + WS5.2.
4. Wave 7: WS6.1 + WS6.2 + WS6.3.
5. Wave 8: WS7.1 + WS7.2 + WS7.3 release closure.

## 4. Gate Checklist for Each Wave
1. Parser/binder/emitter unit tests added for every new behavior.
2. Generator output snapshots updated with deterministic ordering.
3. Integration sample build still passes with SourceGen backend.
4. No new warning-to-error regressions in strict mode.
5. Execution tracker updated with explicit validation command results.
