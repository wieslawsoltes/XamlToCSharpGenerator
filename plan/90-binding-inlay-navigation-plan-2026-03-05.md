# 90 Binding Inlay Navigation Plan (2026-03-05)

## Goal
Add more semantically correct XAML inlay type hints in the language service and make hinted types navigable in VS Code without introducing a second heuristic-only analysis path.

## Current State

### Existing inlay hint coverage
- `XamlInlayHintService` emits only binding result-type hints backed by `ResolvedCompiledBindingDefinition`.
- That means coverage is limited to bindings the semantic binder registers into `ResolvedViewModel.CompiledBindings`.
- Expression bindings are explicitly skipped because `ResultTypeName` is not currently produced.

### Existing definition/reference coverage
- `XamlDefinitionService` and `XamlReferenceService` already resolve:
  - element/type tokens
  - `x:Class`, `x:DataType`
  - selector type/property tokens
  - markup extension class/type tokens
  - binding path property/type tokens via `XamlBindingNavigationService`
- `XamlBindingNavigationService` can already resolve binding source types from:
  - inherited `x:DataType`
  - `ElementName`
  - `RelativeSource Self`
  - `RelativeSource AncestorType=...`
  - binding-path cast/attached-owner type tokens

## Gaps
1. Inlay hints are emitted as plain string labels, so the hinted type text is not itself a navigable surface.
2. Bindings that are semantically resolvable but not registered as compiled bindings do not produce hints.
   - especially `Binding ElementName=...`
   - `Binding RelativeSource={RelativeSource ...}`
   - normal `Binding ...` when binder-side compiled-binding registration does not apply
3. Type-definition location resolution is duplicated inside `XamlDefinitionService`, so `XamlInlayHintService` cannot reuse the same contract cleanly.

## Non-Goals For This Slice
- Do not add heuristic hints for expression bindings until expression semantics expose a stable result type.
- Do not add custom VS Code-side hover/click interception when standard LSP inlay label parts are sufficient.
- Do not guess `Binding Source=...` object types from arbitrary markup values.

## Implementation Plan

### Phase A - Shared CLR navigation location resolver
- Extract reusable type-definition location resolution from `XamlDefinitionService` into a dedicated service/helper.
- Reuse the same source/source-link/metadata fallback order already used by definitions.
- Keep property-definition logic unchanged in this slice unless extraction is required by type-resolution reuse.

### Phase B - Binding semantic result contract
- Extend `XamlBindingNavigationService` with a reusable result contract for binding markup:
  - resolved source type name
  - resolved result type name
  - result type definition location
  - attribute value span to anchor the inlay hint
- Resolve only when binding semantics are deterministic from current existing navigation rules.
- Use the existing binding-path tokenizer and source-type resolver; do not introduce string-shape guesses.

### Phase C - Navigable inlay hint model
- Extend the language-service inlay-hint model to support label parts.
- Emit the trailing type name as a label part with a definition location.
- Keep the existing plain-text label string for engine-level tests and fallback serialization when needed.
- Update LSP serialization to send `label: InlayHintLabelPart[]` when parts are present.

### Phase D - Additional hint coverage
- Preserve current compiled-binding hints.
- Add binding-result hints for semantically resolvable runtime bindings discovered from XML attribute scan:
  - `Binding ElementName=..., Path=...`
  - `Binding RelativeSource={RelativeSource Self}, Path=...`
  - `Binding RelativeSource={RelativeSource AncestorType=...}, Path=...`
  - inherited-`x:DataType` normal bindings when binder-side compiled-binding registration does not already cover them
- Deduplicate by final position + rendered label.

### Phase E - Guard tests
- Add engine tests for:
  - navigable label parts on existing compiled-binding hints
  - element-name binding hints
  - ancestor-type binding hints
- Add LSP tests for:
  - `textDocument/inlayHint` returning label parts with `location`
  - additional binding coverage above
- Keep the existing language-service test slice green.

## Expected Outcome
- More type hints in real XAML binding scenarios.
- Hinted type text becomes clickable/navigable in VS Code through standard LSP support.
- Definition/reference and inlay hint behavior stay aligned because they share the same binding/type semantic resolver.
