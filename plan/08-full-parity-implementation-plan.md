# Full Parity Implementation Plan (XamlIl -> C# SourceGen)

## 1. Goal
Achieve feature-complete parity (for C# Avalonia apps) with Avalonia XamlIl observable behavior while emitting deterministic C# source instead of IL weaving.

## 2. Strategy
Use an ordered pass pipeline that mirrors Avalonia XamlIl transform stages, but targets a C# emission IR:

1. Parse AXAML into rich syntax IR.
2. Run ordered semantic/transform passes.
3. Build cross-file graph (resources/includes/styles/templates).
4. Emit per-document C# populate/build code + shared registry/runtime code.
5. Validate parity with transformer-level acceptance tests against Avalonia reference behaviors.

## 3. Workstreams

### WS1: Language Front-End Parity
Deliverables:
1. Rich node model for directives, markup extensions, property elements (object + text), template/deferred nodes.
2. Namespace/type resolver parity for default Avalonia XML namespace and `clr-namespace` variants.
3. Intrinsic conversion layer equivalent to `AvaloniaXamlIlLanguageParseIntrinsics`.

Exit criteria:
1. Complex literals (Thickness/GridLength/FontFamily/Uri/options) parse and emit without runtime crashes.
2. Directives (`x:Key`, `x:DataType`, `x:ClassModifier`, `x:Shared`) are preserved in IR and consumed by passes.

### WS2: Transform Pass Framework
Deliverables:
1. Pass engine with explicit ordering, pass context, diagnostics hooks, and deterministic outputs.
2. Pass buckets aligned with Avalonia sequence: early directives, property resolution, style/template/binding passes, late metadata cleanup.
3. Pass-level golden tests and ordering contract tests.

Exit criteria:
1. Every upstream XamlIl transformer has an equivalent pass ID and status.
2. Pass graph supports insertion points without rewriting existing passes.

### WS3: Binding and Query Parity
Deliverables:
1. Full binding parser (path grammar including indexers, ancestor/element-name, commands, casts).
2. Compiled-binding accessor generation parity with fallback policy.
3. Query/ResolveByName parity (`#name`, parent/templated-parent features).

Exit criteria:
1. Binding path corpus from Avalonia samples passes.
2. Compiled vs reflection binding behavior matches expected mode/diagnostics contracts.

### WS4: Styles, Selectors, Setters, ControlTheme
Deliverables:
1. Selector parser + normalized selector IR.
2. Setter target metadata and setter transform parity.
3. Style/control-theme runtime object materialization from generated C# (not metadata-only).

Exit criteria:
1. Selector-heavy style samples render equivalent visuals.
2. Duplicate setter/style validation diagnostics align with Avalonia behavior.

### WS5: Template and Deferred Content
Deliverables:
1. Deferred-content IR and C# emitter equivalent to template content behavior.
2. DataTemplate/ControlTemplate/ItemsPanelTemplate/TreeDataTemplate parity paths.
3. Template target-type metadata and priority semantics.

Exit criteria:
1. Templates build and render without `TemplateContent` runtime exceptions.
2. ControlTemplate target/type checks and priority behavior match Avalonia.

### WS6: Resources, Includes, Group Transforms
Deliverables:
1. Cross-file resource graph and include resolution.
2. Group transforms equivalent to merge/include behavior.
3. Resource dictionary capacity and ordering semantics parity.

Exit criteria:
1. `ResourceInclude` and `MergeResourceInclude` semantics match Avalonia for same assembly and external assembly resources.
2. Theme dictionary merge behavior matches expected lookup precedence.

### WS7: Runtime Scope/NameScope/SourceInfo Parity
Deliverables:
1. Root-object scope service model equivalent to XamlIl runtime scope expectations.
2. Name scope registration parity and `FindControl` behavior compatibility.
3. Source info mapping parity for diagnostics/devtools.

Exit criteria:
1. Name scope behavior is equivalent across nested templates/scopes.
2. Source-info surfaces file/line metadata for generated graph nodes consistently.

### WS8: Build Integration and Compatibility Hardening
Deliverables:
1. Full MSBuild backend compatibility matrix (`XamlIl`/`SourceGen`) with predictable toggles.
2. Integration test harness against real Avalonia app fixtures.
3. Migration docs and compatibility guide.

Exit criteria:
1. `SourceGen` backend builds representative apps with no `CompileAvaloniaXamlTask` execution.
2. Generated code remains deterministic across machines/casing differences.

## 4. Milestone Plan

## M1 (Foundation)
1. Finalize pass-engine infrastructure.
2. Upgrade front-end IR for directives/markup/deferred content.
3. Add pass-level test harness and baseline parity corpus ingestion.

## M2 (Language + Bindings)
1. Complete binding/query parser and compiled-binding transform parity.
2. Add converter/intrinsic layer parity.
3. Stabilize diagnostics mapping and location fidelity.

## M3 (Styles + Templates)
1. Implement selector/setter/control-theme transformed emission.
2. Implement deferred template content emission.
3. Validate style/template parity with Avalonia fixture set.

## M4 (Resources + Includes + Scope)
1. Implement group transforms and resource merge materialization.
2. Implement root scope/name scope/source info parity.
3. Validate include/merge parity scenarios.

## M5 (Hardening + Release)
1. Performance and incremental invalidation tuning.
2. End-to-end integration tests and migration docs.
3. Release candidate packaging and parity sign-off.

## 5. Test Gates (Mandatory)
1. Unit:
   - parser grammar, pass contracts, literal conversion, selector/query parsing.
2. Generator:
   - snapshot determinism, diagnostics IDs/locations, incremental invalidation.
3. Integration:
   - sample and fixture apps build/run on `SourceGen` backend.
4. Parity:
   - behavior-by-behavior comparison against Avalonia XamlIl outputs for fixture corpus.
5. Performance:
   - full-build and incremental-build benchmarks tracked against baseline.

## 6. Current Implementation Baseline
Already available and retained as baseline:
1. SourceGen MSBuild backend switching and AdditionalFiles wiring.
2. Generated `InitializeComponent` and populate/build structure.
3. Basic object graph creation, attached property emission, routed event hookup.
4. Partial compiled-binding metadata and diagnostics.
5. Newly added property-element assignment, keyed dictionary add path, and runtime binding fallback emission.

## 7. Definition of Done for "Fully Implemented"
1. Every transformer/group-transformer in Avalonia's `AvaloniaXamlIlCompiler` has an implemented C# pass with passing acceptance tests.
2. Runtime behavior for supported C# apps matches Avalonia XamlIl for object graph, bindings, styles, templates, resources, includes, and diagnostics.
3. Backend is shippable as standalone NuGet with migration guidance and no dependency on XamlX in the SourceGen compiler path.

