# SourceGen Feature-Completion Master Plan (Remaining Work)

## 1. Goal
Reach production-ready feature completeness for C# Avalonia apps with deterministic C# generation that is behaviorally equivalent to the current Avalonia XamlIl/XamlX path for supported scenarios.

Definition of complete:
1. Every transformer/group-transformer in current Avalonia pipeline is either fully implemented or explicitly out-of-scope with documented migration behavior.
2. Runtime behavior parity is validated with fixture-based differential tests (`XamlIl` vs `SourceGen`) for bindings, styles, templates, resources, includes, namescopes, diagnostics, and hot reload.
3. SourceGen backend is shippable as standalone package with migration docs and quality gates.

## 2. Remaining Workstreams

### WS1: Parser and Document Model Parity
Current gap:
1. Parser still assumes class-backed document flow and drops classless XAML files from generation.
2. Property/directive coverage remains subset-only.

Deliverables:
1. Classless document support with generated artifact type (module initializer + URI registry + object graph factory).
2. Richer node model for language directives and advanced property element forms.
3. Parse diagnostics aligned to line/column with parity IDs.

Acceptance:
1. Classless `ResourceDictionary` and include targets are generated and discoverable by URI.
2. Parser tests cover malformed XML, directives, namespace aliases, and classless cases.

### WS2: Compiled Binding Full Grammar and Query Parity
Current gap:
1. Stream operator (`^`) not implemented.
2. Compiled-binding query/plugin semantics are incomplete.
3. Binding options are a subset vs full Avalonia behavior.

Deliverables:
1. Full path grammar support: casts, methods, indexers, attached segments, transforms, stream semantics.
2. Query transform equivalence for `#name`, `$parent`, `$self`, templated-parent paths in compiled-binding mode.
3. Extended binding argument/value option parity (converter, fallback, formatting, null handling, etc.).

Acceptance:
1. Binding differential fixture corpus passes against XamlIl baselines.
2. `AXSG011x` diagnostics match expected shapes and locations.

### WS3: Style/Selector/Setter/ControlTheme Runtime Equivalence
Current gap:
1. Selector coverage has improved but advanced functions/predicates remain.
2. Setter value and priority semantics are incomplete.
3. ControlTheme runtime behavior is partially metadata-oriented.

Deliverables:
1. Full selector parser/IR and runtime expression lowering parity.
2. Setter pipeline parity including value transforms and precedence behavior.
3. ControlTheme materialization and diagnostics parity.

Acceptance:
1. Selector-heavy and control-theme-heavy fixtures produce equivalent behavior.
2. Style diagnostics (`AXSG03xx`) match baseline expectations.

### WS4: Templates and Deferred Content
Current gap:
1. DataTemplate path exists, but full deferred content model parity is missing.
2. ControlTemplate/ItemsPanelTemplate/TreeDataTemplate parity is incomplete.

Deliverables:
1. Deferred content IR and emitter model with lifecycle parity.
2. Template family runtime construction parity and checker coverage.
3. Control template target/parts/priority checker equivalents.

Acceptance:
1. Template fixtures render and behave identically under SourceGen.
2. Template diagnostics (`AXSG05xx`) align with baseline.

### WS5: Resources, Includes, Group Transform Materialization
Current gap:
1. Cross-file includes/merges are still partially metadata-only.
2. Resource dictionary ordering and merge precedence parity is incomplete.

Deliverables:
1. Include/merge group transform materialization with resolved cross-file graph.
2. StaticResource/DynamicResource resolution parity with scope/theme lookup behavior.
3. Resource dictionary ordering/capacity and lookup precedence parity.

Acceptance:
1. Include/merge fixture corpus passes with parity against XamlIl.
2. No fallback-only behavior for resource/include paths in supported cases.

### WS6: Scope, NameScope, SourceInfo, Runtime Services
Current gap:
1. Root namescope path exists; nested/template namescope parity is incomplete.
2. Source-info output exists but not fully equivalent runtime mapping.

Deliverables:
1. Root-scope and nested/template namescope behavior parity.
2. SourceInfo mapping parity for diagnostics/devtools/hot reload references.
3. Runtime service dispatch parity for generated URI loads.

Acceptance:
1. `FindControl` and namescope behavior matches baseline across nested/template scopes.
2. Source info parity tests pass for line/column and node identity mapping.

### WS7: Build Integration, Packaging, Differential Validation
Current gap:
1. Need full parity matrix closure and release gating.
2. Need deterministic and performance guarantees under CI.

Deliverables:
1. Differential test harness (`XamlIl` vs `SourceGen`) with representative app corpus.
2. Performance baselines for full and incremental builds.
3. Packaging hardening and migration guide with compatibility matrix.

Acceptance:
1. All mandatory parity rows move to `Implemented`.
2. CI gates include differential, perf, and determinism checks.

## 3. Execution Sequencing

### Wave 1 (Immediate)
1. Implement classless AXAML generation path (WS1 foundation) so include/resource-only files are no longer dropped.
2. Add tests for classless parse + generation + URI registration.
3. Validate full solution/tests/sample build.

### Wave 2
1. Expand binding option parity and compiled binding query/plugin behavior (WS2).
2. Add negative/positive grammar and diagnostics regression suite.

### Wave 3
1. Complete selector/setter/control-theme runtime materialization (WS3).
2. Close style diagnostic and precedence gaps.

### Wave 4
1. Implement deferred-content/template parity layer (WS4).
2. Add template lifecycle and checker fixtures.

### Wave 5
1. Materialize include/merge group transforms + resource precedence (WS5).
2. Add cross-assembly and nested merge fixture coverage.

### Wave 6
1. Finalize namescope/source-info parity and runtime service behavior (WS6).
2. Differential parity + performance + packaging hardening (WS7).

## 4. Quality Gates (Per Wave)
1. Unit tests for parser/binder/emitter deltas.
2. Generator output tests for deterministic emitted C#.
3. Integration tests and sample validation.
4. No unresolved regressions in existing passing tests.
5. Every closed item mapped back to parity matrix row(s).

## 5. Risks and Controls
1. Risk: widening parser/model breaks existing generated shape.
   Control: snapshot tests and compatibility assertions.
2. Risk: advanced selector/query semantics introduce ambiguous lowering.
   Control: explicit IR + deterministic ordering + tie-break rules.
3. Risk: runtime materialization differences from XamlIl.
   Control: differential fixtures and golden behavior assertions.

