# AvaloniaSemanticBinder De-Hack Wave 2 (XamlX Parity Aligned)

## Objective
Remove remaining heuristic/hack behavior from `AvaloniaSemanticBinder` and replace it with typed semantic decisions aligned with Avalonia XamlIl/XamlX transformer contracts.

This wave is specifically targeted at the remaining binder paths that still rely on:
- expression text scanning,
- string-shape detection,
- compatibility fallbacks that hide semantic mismatches.

## Reference Baseline (Avalonia/XamlX Integration)

### Build and compiler integration seams
- `AvaloniaBuildTasks.targets` drives `CompileAvaloniaXamlTask` and key knobs (`CreateSourceInfo`, `DefaultCompileBindings`, `SkipXamlCompilation`), see:
  - `/Users/wieslawsoltes/GitHub/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets:128`
  - `/Users/wieslawsoltes/GitHub/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets:155`
- `XamlCompilerTaskExecutor` constructs the XamlIl language + compiler + diagnostics and loader dispatcher, see:
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:178`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:210`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:231`

### Transformer order contract (must remain parity anchor)
Avalonia XamlIl transformer ordering in:
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs:43`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs:61`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs:85`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs:98`

Key reference transformers for this wave:
- `AvaloniaXamlIlResolveByNameMarkupExtensionReplacer`
- `AvaloniaXamlIlSetterTransformer`
- `AvaloniaXamlIlBindingPathParser`
- `AvaloniaXamlIlBindingPathTransformer`
- `AvaloniaXamlIlPropertyPathTransformer`
- `AvaloniaXamlIlSelectorTransformer`
- `AvaloniaXamlIlConstructorServiceProviderTransformer`
- `AddNameScopeRegistration`
- `AvaloniaXamlIlRootObjectScope`
- Group transformers: `AvaloniaXamlIncludeTransformer`, `XamlMergeResourceGroupTransformer`

## Remaining Hack Inventory (Current Binder)

### BH-01: Markup context requiredness inferred by expression string scanning
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:2802`
- Current pattern: `ContainsMarkupContextTokens(...)` checks for token substrings in emitted expressions.
- Target: carry `RequiresServiceProvider/RequiresParentStack/RequiresTargetContext` as typed flags end-to-end.

### BH-02: Binding object classification by type-name suffix
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:7057`
- Current pattern: `"Binding"`, `"MultiBinding"`, `.EndsWith(".Binding")`.
- Target: symbol identity checks against resolved Avalonia binding symbols.

### BH-03: ResolveByName conversion gated by lexical checks
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:5538`
- Current pattern: reject by whitespace/markup-shape before semantic flow.
- Reference behavior: explicit markup replacement transformer (`ResolveByNameMarkupExtensionReplacer`) in XamlIl.
- Target: normalize literal + markup forms through one typed resolver.

### BH-04: Name registration discovery via textual assignment parsing
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:5042`
- Current pattern: textual `Name` assignment scanning, skip on parsed ME.
- Reference behavior: dedicated AST manipulation (`AddNameScopeRegistration`, root scope emitter).
- Target: parser/binder emits explicit name-registration semantic nodes without lexical probes.

### BH-05: Ad-hoc markup extension parser is still binder-owned and permissive
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:13631`
- Current pattern: split-based parser with local grammar assumptions.
- Target: move to single canonical markup-expression parser service (Core), return typed ME AST; binder consumes typed nodes only.

### BH-06: Implicit expression detection via pattern heuristics
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:12945`
- Current pattern: starts-with/operator/member-shape checks.
- Target: explicit opt-in expression markers and parser-level disambiguation; minimize lexical inference.

### BH-07: Type resolution compatibility fallback chain still dominant
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:14549`
- Current pattern: default namespace + extension suffix + implicit project namespace compatibility probing.
- Reference behavior: XamlX `TypeReferenceResolver` + xmlns maps as primary.
- Target: strict resolver first; compatibility fallback behind explicit mode and narrower policy.

### BH-08: Setter conversion fallback policy still duplicated and permissive
- Files:
  - object property flow: `.../AvaloniaSemanticBinder.cs:1669`
  - style setter flow: `.../AvaloniaSemanticBinder.cs:3503`
  - control theme setter flow: `.../AvaloniaSemanticBinder.cs:3957`
- Current pattern: repeated fallback branches (`UnsetValue`, compatibility string literal).
- Reference behavior: setter typing enforcement + explicit overload selection in `AvaloniaXamlIlSetterTransformer`.
- Target: shared policy engine with deterministic strict/compat modes and no duplicated branch logic.

### BH-09: Mixed conversion modes (typed conversion + runtime fragment fallback) not centrally governed
- Files:
  - object assignment: `.../AvaloniaSemanticBinder.cs:1684`
  - style setter: `.../AvaloniaSemanticBinder.cs:3491`
- Target: central conversion policy pipeline with explicit ordering and audit metadata.

### BH-10: Event binding path checks still rely on lexical parse rejection
- File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:8205`
- Target: typed event-binding argument model with explicit allowed source kinds.

## Implementation Plan

## WS1: Typed Semantic Flags (replace expression inspection)
1. Add `ResolvedValueRequirements` model in Core:
   - `NeedsServiceProvider`
   - `NeedsParentStack`
   - `NeedsProvideValueTarget`
   - `NeedsRootObject`
   - `NeedsBaseUri`
2. Thread this model through all conversion helpers and resolved assignment/setter nodes.
3. Remove `ContainsMarkupContextTokens` usage from binder decisions.

## WS2: Canonical Markup Expression Parser
1. Move markup parser to Core (`MarkupExpressionParser`) with typed AST nodes.
2. Replace direct `TryParseMarkupExtension` callsites in binder with typed parser facade.
3. Validate nested arguments, escaped braces, named/positional ordering parity.
4. Keep one compatibility gate for legacy quirks, isolated behind explicit option.

## WS3: ResolveByName and NameScope parity
1. Replace `TryBuildResolveByNameLiteralExpression` lexical gating with:
   - typed literal-name token,
   - typed markup-extension node path.
2. Introduce binder semantic node for name registration/write that maps to emitter/runtime scopes.
3. Ensure template scopes and deferred scopes map to root/object scopes consistently.

## WS4: Setter conversion policy unification
1. Create shared `SetterValueConversionPolicy` service used by:
   - object-property setter-like path,
   - style setters,
   - control-theme setters.
2. Encode policy modes:
   - strict (error),
   - compatibility (explicit fallback),
   - runtime-fragment (explicitly tagged).
3. Remove duplicated fallback branches and make diagnostics policy-driven.
4. Align overload/materialization behavior with `AvaloniaXamlIlSetterTransformer` semantics.

## WS5: Binding/PropertyPath/Selector typed pipelines
1. Binding:
   - replace name-based binding object detection with symbol-based checks.
   - unify binding source conflict validation path.
2. PropertyPath:
   - align parser behavior and errors to property path transformer semantics.
3. Selector:
   - keep grammar parse first; eliminate residual untyped fallback conversions.
   - enforce typed property conversion in selector predicates with consistent diagnostics.

## WS6: Type resolution de-heuristic pass
1. Resolution order (strict):
   - explicit prefix xmlns map,
   - configured aliases,
   - `XmlnsDefinitionAttribute` map,
   - explicit `clr-namespace`,
   - intrinsic directives/types.
2. Compatibility fallback:
   - optional only,
   - emits explicit diagnostics on ambiguity/fallback usage,
   - disallow silent extension-suffix probing in strict mode.
3. Add deterministic trace metadata for each resolved type (`ResolutionStrategy`).

## WS7: Build-task and backend contract alignment checks
1. Keep SourceGen backend honoring equivalent knobs:
   - compiled bindings default,
   - source info emission,
   - skip compile.
2. Validate no regressions in backend switching while de-hacking binder.
3. Ensure diagnostics remain mappable to AXSG space with location fidelity.

## WS8: Differential parity test wave (mandatory)
1. Add transformer-mapped differential fixtures for:
   - ResolveByName attribute/property,
   - setter value typing/overload behavior,
   - selector/property-path parse and conversion,
   - binding source conflict handling,
   - name scope registration in templates/deferred content.
2. Add runtime probes (headless) for:
   - template apply with `ElementName` and `TemplateBinding`,
   - style removal/reapply stability under hot reload,
   - Fluent resource lookup/template realization for selected controls.
3. Add “de-hack guards”:
   - assertions that banned helper paths are unused (`ContainsMarkupContextTokens`, suffix binding type checks).

## Execution Phases

### Phase A (WS1 + WS2)
- Introduce typed requirements model and canonical ME parser.
- Migrate conversion callsites.

### Phase B (WS3 + WS4)
- ResolveByName/name-scope semantics.
- Unified setter conversion policy.

### Phase C (WS5 + WS6)
- Binding/property-path/selector typed unification.
- Strict resolver-first type resolution.

### Phase D (WS7 + WS8)
- Build/integration contract verification.
- Differential runtime parity test wave and de-hack regression gates.

## Acceptance Criteria
1. No binder decisions depend on emitted expression substring scanning.
2. No type-name suffix checks for binding object classification.
3. ResolveByName and NameScope behavior passes differential fixtures against XamlIl behavior for covered cases.
4. Setter conversion fallback behavior is single-policy and consistent across object/style/control-theme.
5. Type resolution strategy is explicit, deterministic, and diagnostics-backed.
6. New parity tests pass for build output and runtime behavior (not diagnostics-only).

## Risks and Mitigation
1. Risk: stricter resolver breaks existing permissive XAML.
   - Mitigation: compatibility mode retained but instrumented; strict mode default for parity validation runs.
2. Risk: parser migration introduces edge regressions.
   - Mitigation: dual-run parser comparison tests in transition period.
3. Risk: fallback tightening breaks Fluent/theme scenarios.
   - Mitigation: Fluent differential fixtures gate each phase before merge.

## Deliverables
1. Binder de-hack commits grouped by WS/Phase.
2. New differential and runtime parity tests.
3. Diagnostic updates documenting selected conversion/resolution strategies.
4. Follow-up report documenting removed heuristic paths and remaining compatibility-only shims (if any).
