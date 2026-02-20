# XAML Expressions (SourceGen) - Analysis and Implementation Plan

## Context
We need robust XAML expression support in the standalone Avalonia source-generator compiler, then expose it in docs and catalog samples.

Scope for this wave:
- Add expression markup parsing compatible with explicit/implicit forms.
- Compile expressions against `x:DataType` and emit generated delegates.
- Materialize runtime bindings so expression values update during runtime (OneWay-style behavior).
- Keep existing markup extension behavior stable and avoid regressions.

## Reference Findings
Key implementation points reviewed:
- `src/Controls/src/SourceGen/CSharpExpressionHelpers.cs`
- `src/Controls/src/SourceGen/SetPropertyHelpers.cs`
- `src/Controls/src/Xaml/MarkupExpressionParser.cs`

Observed behavior:
- Explicit syntax: `{= ...}`.
- Implicit expression detection for unambiguous C# inside `{ ... }`, with guards to avoid taking over markup extensions.
- Typed binding generation for expression values when `x:DataType` is available.
- Diagnostic handling for ambiguous/member-resolution issues.

## Current Generator Gap
Current compiler path treats `{...}` as markup extensions/bindings/primitives. Arbitrary expression payloads are not converted into generated typed bindings.

## Target Behavior
1. Expression forms
- Explicit: `{= FirstName + " " + LastName}`.
- Implicit (safe heuristic): `{FirstName + "!"}`, `{Price * TaxRate}`, `{IsEnabled AND IsVisible}`.

2. Binding semantics
- Expressions target `x:DataType` source.
- Generated code emits typed delegates and runtime expression-binding objects.
- Runtime updates re-evaluate on source/property change (best effort with dependency filtering).

3. Diagnostics
- Missing `x:DataType` for expression binding: AXSG0110.
- Invalid expression rewrite/compile for `x:DataType`: AXSG0111.

4. Safety
- Known markup extensions keep precedence.
- Bare identifiers become expressions only when no markup-extension resolution is possible.

## Implementation Design

### Binder
- Add expression markup classifier:
  - explicit `{= ...}`
  - implicit expression heuristic with markup-extension guard.
- Add expression normalization:
  - operator aliases: `AND/OR/LT/GT/LTE/GTE`
  - quote normalization for single-quoted multi-char strings.
- Add expression rewrite pass:
  - rewrite source-member identifiers to generated source parameter access (`source.Member`).
  - collect root dependency names.
- Compile-validate rewritten expression via Roslyn in-context.
- Integrate into Avalonia property assignment path before regular binding parse.
- Register expression delegate in compiled binding registry metadata.

### Runtime
- Add `SourceGenExpressionBinding<TSource>` implementing `IBinding`.
- Provide observable-driven updates:
  - evaluates expression against DataContext (or explicit source path later).
  - listens for DataContext and `INotifyPropertyChanged` updates.
  - applies dependency filtering if known.
- Add `SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<TSource>(...)` factory.

### Emitter
- Extend binding-expression detection so expression runtime calls are emitted through Avalonia binding indexer assignment path.

### Tests
- Generator tests for:
  - explicit expression emission.
  - implicit expression emission.
  - missing `x:DataType` diagnostic.
- Assert generated call sites include expression runtime factory and compiled-binding registration.

### Docs/Samples
- README: add expression section with syntax and constraints.
- Catalog sample: add expression examples in Markup Extensions tab.

## Out of Scope (this wave)
- Full ambiguity diagnostics matrix.
- Deep nested-property dependency graph tracking.
- TwoWay expression binding/source writes.

## Exit Criteria
- `dotnet test` passes for updated generator/runtime tests.
- Catalog sample builds and demonstrates non-empty expression examples.
- README documents usage and limitations.
