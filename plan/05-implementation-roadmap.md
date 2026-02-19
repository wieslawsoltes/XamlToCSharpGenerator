# Implementation Roadmap

## 1. Reality Check (Current State)
The backend is functional for a subset of XAML scenarios but is not yet full XamlIl parity.

Implemented baseline:
1. Backend switching, AdditionalFiles wiring, and generated partial scaffolding.
2. Object graph generation for core content/children/items patterns.
3. Attached property and basic routed event emission.
4. Partial compiled-binding metadata and diagnostics.

Implemented in latest iteration:
1. Property-element object assignment and collection-add emission.
2. Keyed dictionary add path (`x:Key` -> `Add(key, value)`).
3. Runtime binding fallback emission for Avalonia-property binding markup.
4. DataTemplate inline content path emitted as generated `FuncDataTemplate`.
5. Compiled-binding accessor path now supports indexer segments.
6. Binding option emission expanded (`Mode`, `ElementName`, `RelativeSource`).
7. Markup extension conversion added (`x:Null`, `x:Type`, `x:Static`, `StaticResource`, `DynamicResource`, `TemplateBinding`) with partial semantics.
8. Style target-type aware `Setter.Property` token resolution.
9. Root NameScope registration and on-demand static-resource helper emission.
10. Direct `Add(...)` child attachment mode for style/setter-like object graphs.

Outstanding parity work:
1. Full transform-pass equivalence with Avalonia XamlIl.
2. Deferred content/template parity.
3. Selector/query/binding full grammar parity.
4. Full include/merge resource materialization and scope semantics.

## 2. Delivery Phases

## Phase 1: Pass Infrastructure and IR Upgrade
Deliver:
1. Ordered pass engine with deterministic pass execution.
2. Rich syntax/semantic IR for directives, markup extensions, deferred nodes.
3. Pass-level diagnostics and test harness.

Exit:
1. Every upstream transformer has an assigned SourceGen pass owner.
2. IR can represent all currently dropped information.

## Phase 2: Language and Binding Core
Deliver:
1. Intrinsic conversion parity layer.
2. Full binding path parser and compiled-binding transform parity.
3. Query/resolve-by-name transform equivalents.

Exit:
1. Binding/query fixture corpus passes.
2. Diagnostics match expected categories and locations.

## Phase 3: Styles, Setters, Themes, Templates
Deliver:
1. Selector parser + transformed style emission.
2. Setter transform and target metadata parity.
3. ControlTheme and template/deferred content parity.

Exit:
1. Style/template-heavy fixtures produce equivalent runtime visuals/behavior.

## Phase 4: Resources, Includes, Scopes, SourceInfo
Deliver:
1. Group transform parity for include/merge.
2. Resource dictionary ordering/capacity semantics.
3. Root scope + namescope + source-info parity.

Exit:
1. Cross-file resource/include fixtures pass parity tests.
2. Name scope behavior matches reference.

## Phase 5: Hardening and Release
Deliver:
1. Full parity regression suite and performance benchmarks.
2. Packaging and migration docs.
3. Release candidate quality gates.

Exit:
1. Mandatory parity matrix rows all marked `Implemented`.
2. Determinism and incremental performance goals met.

## 3. Execution Rules
1. No feature marked complete without both positive and negative tests.
2. Keep default backend unchanged unless `SourceGen` is explicitly selected.
3. Maintain C#-only v1 scope while building extensibility for non-C# phases.

## 4. Planning References
1. Transformer analysis: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/07-avalonia-xamlil-transform-analysis.md`
2. Full parity plan: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/08-full-parity-implementation-plan.md`
