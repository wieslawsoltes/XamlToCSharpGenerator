# Typed Binding/Template Parity Plan (Heuristic Removal Wave)

## Problem statement

Current SourceGen emission still contains heuristic paths where binding/template behavior is inferred from generated C# text shapes (for example `StartsWith("new ...Binding(")`) instead of from typed semantic information produced by the binder.

This creates parity risk against Avalonia/XamlX and can regress scenarios such as:

- `TemplateBinding` inside standalone `ControlTemplate TargetType=...`
- Binding-specific NameScope attachment
- Binding-vs-value assignment path selection (`IndexerDescriptor` vs `SetValue`)

## Goal

Replace emitter-side string-shape detection with binder-produced typed value metadata, while preserving current runtime behavior and improving XamlX parity determinism.

## Non-goals

- Replacing the entire markup parser in this wave.
- Removing runtime fallback mechanisms (`ProvideRuntimeXamlValue`) in this wave.
- Reworking all value conversion logic into a full expression AST in this wave.

## Workstreams

### WS1 - Typed value-kind metadata in semantic model

1. Add `ResolvedValueKind` enum in core models.
2. Extend `ResolvedPropertyAssignment` with `ValueKind`.
3. Extend `ResolvedSetterDefinition` with `ValueKind` (for consistency/future emitter usage).
4. Extend `ResolvedObjectNode` with `IsBindingObjectNode`.

Acceptance:

- Model changes compile with default-compatible constructors.
- Existing call sites continue to compile.

### WS2 - Binder classification (source of truth)

1. Classify assignment value kinds in binder as they are resolved:
   - `Binding` for `Binding`/`CompiledBinding`/reflection/expression binding outputs.
   - `TemplateBinding` for template-binding outputs.
   - `DynamicResourceBinding` for dynamic resource outputs.
   - `MarkupExtension` for non-binding markup extension values.
   - `Literal` for primitive/object literal conversion outputs.
   - `RuntimeXamlFallback` when runtime fragment fallback is used.
2. Mark `ResolvedObjectNode.IsBindingObjectNode` from resolved node type (Binding/MultiBinding nodes).
3. Keep conversion behavior unchanged; only enrich with typed metadata.

Acceptance:

- Binder emits stable typed metadata for all property-assignment creation paths.
- Existing diagnostics remain unchanged.

### WS3 - Emitter heuristic removal

1. Replace `LooksLikeBindingExpression(...)` call sites in assignment emission with `ValueKind` checks.
2. Replace `LooksLikeBindingObjectNode(...)` usage with `ResolvedObjectNode.IsBindingObjectNode`.
3. Keep a minimal compatibility helper only where still unavoidable; no control-flow decisions should depend on raw generated-string prefixes for assignment category.

Acceptance:

- Binding assignment path selection uses semantic metadata.
- NameScope attachment for binding values uses semantic metadata.
- Generated output for template/binding parity scenarios remains equivalent or improved.

### WS4 - Parity/regression test coverage

1. Add/extend generator test covering standalone `ControlTemplate` with `TemplateBinding`.
2. Assert no fallback string-literal emission for template-binding text properties.
3. Run focused differential suites and full test project.

Acceptance:

- New regression tests pass.
- Differential and full test suites pass.

## Execution order

1. WS1 model types.
2. WS2 binder classification.
3. WS3 emitter migration.
4. WS4 tests and validation.

## Risks and mitigations

- Risk: missed assignment call sites default to `Unknown`, causing accidental non-binding path.
  - Mitigation: classify all `ResolvedPropertyAssignment` creation sites and keep conservative fallback.
- Risk: behavior drift in theme/style setter flows.
  - Mitigation: add setter metadata now, use in future wave if needed; validate Fluent differential suite.

## Done criteria

- No emitter path uses string prefix matching to decide binding-vs-value assignment for property assignments.
- Standalone `ControlTemplate` + `TemplateBinding` scenario emits binding objects, never string literals.
- Full tests pass on current workspace test suite.
