# Full XamlIl-to-C# Parity Execution Plan (v2)

## Objective
Deliver full C# source-generated parity for C# Avalonia applications versus current Avalonia XamlIl behavior, without XamlX dependency in the SourceGen compiler path.

## Architecture direction
1. Introduce explicit ordered pass pipeline equivalent to Avalonia transformer order.
2. Keep parser/binder/emitter split, but add pass contracts and pass context (type system, diagnostics, options, cross-file graph).
3. Emit deterministic C# object graph + runtime bootstrapping with no IL weaving.
4. Preserve backend opt-in (`XamlIl` default, `SourceGen` opt-in).

## Work packages

### WP1: Pass engine and parity bookkeeping
Deliverables:
1. `IXamlTransformPass` abstraction and ordered pass runner.
2. Pass IDs mapped one-to-one to each Avalonia transformer/group-transformer.
3. Pass-level diagnostics contract and execution trace for tests.

Exit criteria:
1. Every upstream transformer has explicit status: implemented/partial/missing in code (not only docs).
2. Execution order is deterministic and test-covered.

### WP2: Binding/query parity
Deliverables:
1. Binding grammar expansion: indexers, casts, plugins, ancestor/element-name semantics.
2. Query transform equivalents for `#name`, `$parent`, and resolve-by-name paths.
3. Compiled-binding metadata and runtime behavior alignment.

Exit criteria:
1. Binding fixture corpus from Avalonia samples passes with matching diagnostics.
2. No fallback-to-string behavior for recognized binding constructs.

### WP3: Setter/style/control-theme parity
Deliverables:
1. Full selector transform/parser equivalent for style and nested style selectors.
2. Setter target metadata + setter value transform parity (`Binding`, `TemplateBinding`, resources, priorities).
3. ControlTheme validation and runtime materialization parity.

Exit criteria:
1. Style-heavy sample apps match XamlIl visual/runtime behavior.
2. Duplicate/invalid style diagnostics align with XamlIl expectations.

### WP4: Template/deferred-content parity
Deliverables:
1. Deferred content model equivalent for templates.
2. DataTemplate/ControlTemplate/TreeDataTemplate/ItemsPanelTemplate parity.
3. ControlTemplate target-type/parts/priority checker equivalents.

Exit criteria:
1. Template-heavy fixtures render correctly with sourcegen-only backend.
2. Template diagnostics match expected parity baseline.

### WP5: Resource/include/group-transform parity
Deliverables:
1. Merge/include group transform materialization (not metadata-only).
2. Static and dynamic resource semantics with correct scope/theme lookup.
3. Resource dictionary capacity and ordering semantics.

Exit criteria:
1. Include/merge fixtures behave identically to XamlIl in lookup precedence.
2. StaticResource/DynamicResource parity coverage passes in integration tests.

### WP6: Scope/source-info/runtime integration parity
Deliverables:
1. Root object scope model equivalent to XamlIl scope behavior.
2. NameScope behavior parity across templates and nested scopes.
3. Source info parity for diagnostics/devtools mapping.

Exit criteria:
1. `FindControl` and namescope behavior matches XamlIl across nested/template contexts.
2. Source-info registry parity tests pass.

### WP7: Build integration and migration hardening
Deliverables:
1. Full backend matrix tests (`XamlIl` vs `SourceGen`).
2. No `CompileAvaloniaXamlTask` execution when SourceGen enabled.
3. NuGet packaging + migration docs + compatibility matrix.

Exit criteria:
1. Representative Avalonia apps build and run on SourceGen backend.
2. Deterministic source output across OS/path-casing scenarios.

## Milestones

### M1 (current+1)
1. Land pass engine and transformer mapping.
2. Move existing binder logic into first-class passes.
3. Keep current behavior parity stable.

### M2
1. Complete binding/query parity surface.
2. Add binding parser fixtures from Avalonia test corpus.

### M3
1. Complete styles/setters/control-theme runtime parity.
2. Validate against selector-heavy fixtures.

### M4
1. Complete template/deferred-content parity.
2. Complete control template checkers.

### M5
1. Complete resources/includes/group-transform parity.
2. Validate merge precedence and theme dictionaries.

### M6
1. Complete scope/source-info/runtime parity.
2. Harden build/backend integration and packaging.

## Validation matrix
1. Unit tests: parser, transform passes, conversion/intrinsic semantics.
2. Generator snapshot tests: deterministic generated C# and diagnostics.
3. Integration fixtures: compile+run with `AvaloniaXamlCompilerBackend=SourceGen`.
4. Differential parity tests: compare behavior and key diagnostics against XamlIl backend.
5. Performance tests: full build and incremental edit benchmarks.

## Current implementation baseline in code
1. Object/property graph emission is active with `Content`/`Children`/`Items`/`DictionaryAdd` and `DirectAdd`.
2. Property-element object assignments are emitted.
3. Attached properties and event hookups are emitted.
4. Binding markup supports core options and compiled-binding accessors now support indexer segments.
5. Markup extension conversion includes `x:Null`, `x:Type`, `x:Static`, `StaticResource`, `DynamicResource`, `TemplateBinding` (partial semantics).
6. NameScope and static-resource helper emission paths are available but partial versus full XamlIl scope/provider behavior.

## Definition of done (full parity)
1. All Avalonia transformers/group-transformers have implemented equivalent passes with acceptance coverage.
2. Runtime behavior parity is validated on fixture corpus across bindings, styles, templates, resources, includes, and diagnostics.
3. SourceGen backend is shippable as standalone NuGet backend with migration documentation and compatibility guarantees.
