# C# Expression Evaluation Expansion (SourceGen) - Spec and Plan

## Context
SourceGen already supported expression markup in selected paths (`{= ...}` and implicit `{ ... }` in many Avalonia-property assignments). The remaining gap was consistent semantic expression handling across additional setter/materialization paths and clearer user-facing controls for expression parsing behavior.

This wave expands expression support while preserving XML/XAML compatibility and keeping markup-extension grammar precedence intact.

## Research Summary
- XAML 2009 markup-extension grammar requires `{ TypeName ... }` tokenization and extension resolution first; expression parsing must not break standard markup-extension interpretation.
- Expression compilation patterns show practical split:
  1. explicit expression form (`{= ...}`) as deterministic opt-in;
  2. implicit expression form (`{ ... }`) under safe heuristics and extension-name guards.
- SourceGen architecture already had expression AST rewrite + Roslyn validation and runtime expression-binding materialization. The main work was expanding where that pipeline is applied and exposing parse-mode switches.

## Goals
1. Keep explicit C# expressions first-class (`{= ...}`).
2. Keep implicit expression mode available but controllable.
3. Extend expression binding semantics into style/control-theme setter flows.
4. Preserve deterministic diagnostics (`AXSG0110`, `AXSG0111`) and avoid fallback regressions.
5. Expand catalog samples with comprehensive expression coverage.

## Non-Goals
1. Dynamic scripting/runtime code compilation APIs.
2. Statement-block execution inside XAML (expressions only).
3. Replacing CLR/EnC rude-edit limits.

## Public Contract Additions
New MSBuild properties:
- `AvaloniaSourceGenCSharpExpressionsEnabled` (default `true`)
- `AvaloniaSourceGenImplicitCSharpExpressionsEnabled` (default `true`)

Behavior:
- `AvaloniaSourceGenCSharpExpressionsEnabled=false`: disable expression parsing completely.
- `AvaloniaSourceGenImplicitCSharpExpressionsEnabled=false`: keep explicit `{= ...}` active; disable implicit `{ ... }` expression detection.

## Technical Design

### 1) Shared expression conversion helper
Create one binder helper that:
1. Detects expression markup with option-aware parse gates.
2. Requires `x:DataType` scope.
3. Builds normalized accessor expression + dependency names.
4. Builds runtime expression-binding call.
5. Returns stable diagnostic band (`AXSG0110`/`AXSG0111`) for caller-specific messages.

### 2) Setter-path expansion
Apply shared helper in:
- style setter binding path
- control-theme setter binding path

When expression succeeds:
- setter uses expression runtime value expression,
- compiled binding registry entry is emitted for parity/telemetry.

### 3) Expression rewrite robustness
Improve local-scope preservation and dependency capture:
- collect expression-local variable names (lambda parameters, query locals, declaration/pattern variables),
- avoid rewriting locals as source-member accesses,
- capture dependencies when expressions explicitly use `source.Member`.

### 4) Parser gating
Expression parse function now takes:
- `expressionsEnabled`
- `implicitExpressionsEnabled`

Explicit `{= ...}` remains valid when implicit mode is off.

## Implementation Plan
1. Add new generator options + build-transitive property wiring.
2. Add shared expression-conversion helper and replace duplicate binder logic.
3. Integrate helper into style/control-theme setter loops.
4. Enhance expression local-scope/dependency handling in rewriter pipeline.
5. Add/extend tests for:
   - style/control-theme expression binding paths,
   - implicit toggle behavior,
   - explicit source-member dependency tracking.
6. Add catalog sample page dedicated to C# expressions.
7. Update README contract/docs.

## Acceptance Criteria
1. Generator tests pass with new expression tests.
2. Catalog and CRUD samples build cleanly.
3. Explicit expressions continue to work with implicit mode disabled.
4. Style/control-theme expression setters contribute compiled binding metadata and runtime expression wiring where materialized.
5. README and build contracts document new user knobs.
