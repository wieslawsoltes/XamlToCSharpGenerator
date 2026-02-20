# XAML Debugger Source Mapping Plan

## Goal
Enable debugger-friendly source mapping from generated C# back to AXAML locations so stack traces, breakpoints, and stepping can point to original XAML source lines, while preserving existing runtime source info registry behavior.

## Current State (Gap Analysis)
1. `AvaloniaSourceGenCreateSourceInfo` currently enables registrations into `XamlSourceInfoRegistry` (kind/identity/file/line/column metadata).
2. Generated code does not emit C# source mapping directives (`#line`) for emitted object graph statements.
3. Result: diagnostics include file/line, but runtime debugging/stepping in generated methods remains anchored to `.g.cs` instead of AXAML source lines.

## Scope
1. Add statement-level AXAML mapping for generated object graph emission paths:
   - object creation/init
   - property assignment
   - event hookup
   - collection/dictionary/materialization statements
   - deferred/template materialization statements
2. Keep mapping opt-in behind existing `AvaloniaSourceGenCreateSourceInfo` to avoid changing default generated output shape.
3. Preserve existing runtime `XamlSourceInfoRegistry` registrations.

## Non-Goals
1. No runtime behavior changes to object graph creation semantics.
2. No CLR Edit-and-Continue rude-edit bypass changes.
3. No new public API surface required for v1; reuse existing option.

## Design
1. Introduce emitter helper to append mapped statement lines:
   - emits `// AXSG:XAML line:column` marker
   - emits `#line <line> "<file>"` before the statement
   - emits `#line default` and `#line hidden` after the statement
2. Propagate mapping context (enabled flag + AXAML path) through:
   - `EmitNode`
   - `TryEmitDeferredTemplateNode`
   - `TryEmitDictionaryMergePropertyElement`
   - `EmitEventSubscription`
3. Use statement-specific line/column where available:
   - node-level statements use node line/column
   - property/event/property-element statements use their own line/column
4. Keep deterministic codegen:
   - stable directive ordering
   - normalized line numbers (`>= 1`)
   - no path randomization

## Implementation Steps
1. Add mapping context initialization in emitter entrypoint (`Emit`):
   - `emitDebugLineDirectives = viewModel.CreateSourceInfo`
   - normalized XAML source path for directives
2. Add helper methods:
   - `AppendSourceMappedLine(...)`
   - `NormalizeLineDirectivePath(...)`
   - `EscapeLineDirectivePath(...)`
   - `ExtractLeadingIndent(...)`
3. Update emission call sites in core graph methods to use mapped append helper instead of direct `AppendLine` for source-correlated statements.
4. Ensure recursive calls pass mapping context consistently.
5. Keep existing `EmitSourceInfoRegistrations` untouched.

## Validation
1. Extend generator tests:
   - When `AvaloniaSourceGenCreateSourceInfo=true`, generated code contains:
     - `#line` directives with AXAML path
     - `// AXSG:XAML` line:column markers
     - existing `XamlSourceInfoRegistry.Register` entries
   - When disabled, generated code does not contain `#line`/`AXSG:XAML` markers.
2. Run:
   - targeted generator tests for source info
   - full `AvaloniaXamlSourceGeneratorTests` suite

## Rollout Notes
1. This is safe for incremental adoption because it is opt-in and tied to existing source-info flag.
2. CLI and IDE debuggers can now map generated execution back to AXAML lines in supported stepping contexts.
