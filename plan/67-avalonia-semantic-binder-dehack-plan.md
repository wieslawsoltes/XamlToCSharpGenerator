# AvaloniaSemanticBinder De-Heuristics Plan

## Goal
Remove remaining heuristic and string-shape logic from `AvaloniaSemanticBinder` and replace it with typed semantic decisions aligned with Avalonia/XAML semantics and XamlX behavior.

## Scope
- In scope: binder parsing/classification/conversion paths, emitted semantic metadata feeding emitter/runtime, parity diagnostics, and regression/differential tests.
- Out of scope: unrelated sample UX work, non-binder runtime feature additions.

## Findings Inventory (Hack/Heuristic Hotspots)

1. Value-kind classification still infers semantics from emitted expression text.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:6798`  
   Example: `ClassifyResolvedValueKind` scans `valueExpression.Contains(...)` / `StartsWith(...)`.

2. Markup-extension detection and dispatch still rely on string shape checks.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:1588`, `:13301`  
   Example: `LooksLikeMarkupExtension("{...}")` gate before semantic parsing.

3. Template/style identification uses name suffix checks.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:4383`, `:1207`, `:4901`  
   Example: `EndsWith("Template")`, `EndsWith(".ControlTemplate")`.

4. Static-resource resolver requirement inferred by scanning generated expression text.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:12355`  
   Example: `assignment.ValueExpression.Contains("__ResolveStaticResource(")`.

5. Resolve-by-name literal conversion uses lexical heuristics.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:5265`  
   Example: rejects tokens using whitespace/markup-shape rather than structured value model.

6. Type resolution still has fallback-heavy candidate probing and extension-name guessing.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:14229`, `:14367`, `:14462`  
   Example: hardcoded namespace seed + `+ "Extension"` probing.

7. Template content validation reparses raw XAML with local heuristics (`.Content` suffix checks).  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:4600`, `:4616`, `:4735`.

8. Setter/object conversion still uses compatibility fallbacks that can hide semantic mismatches.  
   File: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs:1668`, `:3438`, `:3855`, `:10031`.

## Target Architecture

1. Every assignment/setter conversion returns a typed conversion result, not just `bool + string`.
2. Markup values are represented as parsed semantic forms (`Binding`, `TemplateBinding`, `StaticResource`, `DynamicResource`, `Reference`, generic ME call).
3. Binder carries explicit feature flags (`UsesStaticResourceResolver`, `UsesParentStack`, `NeedsNameScope`) instead of expression-text scanning.
4. Template/style/control-theme detection is symbol-based and/or parser-node-kind-based, never suffix-based.
5. Validation runs on parsed document model nodes, not raw-XAML reparsing where avoidable.

## Implementation Workstreams

### WS1: Typed Conversion Result Model
1. Introduce `ResolvedValueConversionResult` in Core models with:
   - `Expression`
   - `ResolvedValueKind`
   - `RequiresRuntimeServiceProvider`
   - `RequiresParentStack`
   - `RequiresStaticResourceResolver`
   - `IsRuntimeFallback`
2. Refactor:
   - `TryConvertValueExpression`
   - `TryConvertMarkupExtensionExpression`
   - binding/template/resource conversion helpers  
   to return this result.
3. Remove string-based classification from `ClassifyResolvedValueKind`; delete method once all callsites migrate.

### WS2: Markup Parsing and Value Normalization
1. Replace `LooksLikeMarkupExtension` gates with parser-first attempt:
   - parse once
   - dispatch by parsed extension name/type
2. Keep compatibility for escaped literals (`{}`) and nested markup arguments via structured tokenizer.
3. Promote parsed markup data into binder node context to avoid reparsing same strings repeatedly.

### WS3: Symbol-Driven Template/Style Recognition
1. Replace `EndsWith(".ControlTemplate")` / `EndsWith("Template")` checks with symbol equality:
   - `Avalonia.Markup.Xaml.Templates.ControlTemplate`
   - `Avalonia.Markup.Xaml.Templates.DataTemplate`
   - `Avalonia.Markup.Xaml.Templates.TreeDataTemplate`
   - `Avalonia.Controls.Templates.ControlTemplate` fallback where applicable
2. Replace `.Content` suffix heuristics with actual property resolution on owner type.
3. Update setter target inference and template part traversal to use typed node categories.

### WS4: Resource/Key Semantics Without Expression Scanning
1. Add typed resource-key model (`String`, `Type`, `StaticMember`, `Object`) in binder outputs.
2. Compute `EmitStaticResourceResolver` from conversion flags collected during binding, not `Contains("__ResolveStaticResource(")`.
3. Ensure resource-key expression generation is deterministic and free of string probing.

### WS5: Type Resolution Hardening
1. Define strict deterministic resolution order:
   - explicit xmlns mapping
   - configured aliases
   - `XmlnsDefinitionAttribute` map
   - explicit `clr-namespace`
   - optional compatibility fallback list (guarded by option)
2. Add ambiguity diagnostics when multiple candidates resolve.
3. Keep existing namespace seed only in compatibility mode; default to standards-driven resolution.

### WS6: Fallback Policy Tightening
1. Replace implicit object/string fallback for unresolved non-string values with policy-driven outcomes:
   - strict: error
   - compatibility: explicit runtime fallback wrapper
2. Restrict `UnsetValue` fallback to setter/property contexts proven by parity tests.
3. Add diagnostics that include conversion strategy chosen (typed, fallback, runtime).

### WS7: Template/Resource Validation on Document Model
1. Stop reparsing `RawXaml` for template-root and part validation when parsed model already has equivalent node graph.
2. For includes/templates requiring raw fragments, map nodes back to original positions through existing line/column metadata.
3. Ensure no `.Content` lexical assumptions remain in validators.

### WS8: Differential Parity and Regression Gates
1. Add binder tests that assert:
   - no expression-text inspection paths
   - no suffix-based template kind logic
   - no markup-shape gate before parser dispatch
2. Extend Fluent differential corpus:
   - template bindings
   - static/dynamic resources
   - control theme setters
   - resolve-by-name/reference cases
3. Add generated-source assertions banning fragile patterns (e.g., literalized `{TemplateBinding ...}`).

## Execution Sequence

1. Phase A: WS1 + WS2 core refactor (typed conversion spine).
2. Phase B: WS3 + WS4 (template/resource semantics).
3. Phase C: WS5 + WS6 (resolution/fallback policy hardening).
4. Phase D: WS7 + WS8 (validation replacement + parity gates).

## Acceptance Criteria

1. `AvaloniaSemanticBinder` no longer classifies value semantics from emitted expression text.
2. No template/style kind detection by `EndsWith("Template")` or equivalent suffix heuristics.
3. Static resource resolver emission is driven by semantic flags only.
4. Differential tests for selected Fluent/control-template/resource cases pass against expected runtime behavior.
5. Existing binding/template regressions remain green, including TemplateBinding materialization paths.

## Risk Controls

1. Ship each phase behind feature toggles where needed for quick bisect (`strict semantic binder mode`).
2. Keep compatibility fallback path until parity suite reaches target pass rate.
3. Add binder trace output for conversion decisions to debug parity gaps quickly.
