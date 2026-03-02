# Avalonia Parse Unification Plan (Compiler-First)

Date: 2026-03-01  
Scope: String-to-object value conversion used by generated code paths in Avalonia binder/emitter/runtime helpers.

## Goals

1. Move string literal conversion for known Avalonia value types into compiler semantics.
2. Replace runtime string parsing hotspots with compile-time validated literal emission where possible.
3. Keep deterministic fallback behavior for unsupported literal shapes without introducing heuristics.
4. Improve parser performance and allocation profile in conversion hot paths.

## Inventory Summary

Current emitted parse hotspots are concentrated in:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  - `Brush.Parse(...)`
  - `TransformOperations.Parse(...)`
  - generic static `Parse(...)` fallback
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.MarkupHelpers.cs`
  - `DateTime.Parse(...)`
  - `TimeSpan.Parse(...)`
  - generic static `Parse(...)`/`Parse(..., IFormatProvider)`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`
  - runtime helper parsing for font feature collections/font family

Reference compiler behavior baseline from Avalonia intrinsic conversion covers:

- `TimeSpan`, `Thickness`, `Point`, `Vector`, `Size`, `Matrix`, `CornerRadius`
- `Color` and `IBrush` color shortcuts
- `RelativePoint`, `GridLength` (+ row/column definitions)
- collection literal conversion with split semantics and typed element coercion

## Phase 1 (First): Parser Quality + Performance Foundation

Deliverables:

1. Add compiler parser semantics for Avalonia intrinsic value literals in Core parsing:
   - numeric token stream parser (comma/whitespace delimited, low-allocation)
   - intrinsic parsers:
     - `Thickness`
     - `CornerRadius`
     - `Point`
     - `Vector`
     - `Size`
     - `Matrix`
     - `Rect`
     - `PixelPoint`
     - `PixelSize`
     - `PixelRect`
     - `Vector3D`
     - `GridLength`
     - `RelativePoint`
     - `RelativeScalar`
     - `RelativeRect`
     - hex color (`#RGB`, `#ARGB`, `#RRGGBB`, `#AARRGGBB`)
2. Ensure parser APIs are deterministic and explicit:
   - no string-shape heuristics
   - no reflection
   - invariant-culture numeric parsing
3. Add unit tests for each parser shape and invalid input behavior.

Acceptance:

- New parser test suite passes.
- No regression in existing parsing semantics tests.

## Phase 2: Binder Intrinsic Conversion Wiring

Deliverables:

1. Add compiler-side intrinsic conversion stage in `TryConvertValueConversion(...)` before generic static parse fallback.
2. Emit canonical constructor/static-member expressions for parsed values:
   - constructor forms where deterministic
   - static members (`GridLength.Auto`) where applicable
3. Preserve existing fallback chain when intrinsic parse fails.

Acceptance:

- Generated expressions for supported intrinsic types avoid generic `.Parse("...")` fallback.
- Existing generator tests continue to pass (updated only where behavior is intentionally improved).

## Phase 3: Runtime Parse Helper Reduction

Deliverables:

1. Replace runtime font feature collection string-splitting helper dependency with compiler-emitted typed collection construction.
2. Keep safe fallback behavior for literal forms that remain runtime-only.

Acceptance:

- Reduced runtime parsing/string-splitting on generated hot paths.
- Runtime tests pass with unchanged behavioral semantics.

## Phase 4: Coverage Parity Sweep

Deliverables:

1. Compare remaining generated `.Parse("...")` sites against known Avalonia parse-capable types.
2. Classify each remaining site:
   - intentional runtime parse (documented)
   - missing compiler conversion (implement)
3. Add regression guard test to prevent reintroduction of ad-hoc parse emission for covered intrinsic types.

Acceptance:

- Documented residual parse calls with rationale.
- Guard tests enforce coverage floor.

## Phase 5: Deterministic Brush Color Lowering

Deliverables:

1. In brush conversion path, lower deterministic color literals (`#RGB/#ARGB/#RRGGBB/#AARRGGBB` and named colors) to `SolidColorBrush(Color)` construction.
2. Keep `Brush.Parse(...)` fallback for non-color brush grammars.
3. Add generator and guard tests proving:
   - deterministic brush-literal lowering works,
   - parse fallback remains for non-color brush values.

Acceptance:

- Generated code for color-only brush literals avoids `Brush.Parse(...)`.
- Non-color literals still use `Brush.Parse(...)` fallback.

## Phase 6: Deterministic TransformOperations Lowering

Deliverables:

1. Add compiler parser semantics for canonical `TransformOperations` function-list literals:
   - `translate`, `translateX`, `translateY`
   - `scale`, `scaleX`, `scaleY`
   - `skew`, `skewX`, `skewY`
   - `rotate`
   - `matrix`
   - `none` identity token
2. Emit deterministic builder-based `TransformOperations` expressions when symbols/contracts support it.
3. Keep `TransformOperations.Parse(...)` fallback for unsupported/invalid literal shapes.
4. Add parser + generator guard tests for deterministic lowering and fallback preservation.

Acceptance:

- Canonical transform literals avoid `TransformOperations.Parse(...)` in generated code.
- Unsupported shapes still use `TransformOperations.Parse(...)` fallback.
- New parser semantics tests pass.

## Phase 7: Deterministic Cursor Lowering

Deliverables:

1. Add compiler parser semantics for deterministic `Cursor` literals targeting `StandardCursorType` members:
   - bare member (`Hand`)
   - owner-qualified (`StandardCursorType.Hand`, `CursorType.Hand`)
2. Emit deterministic `new Cursor(StandardCursorType.Member)` expressions when contracts/symbols are available.
3. Keep `Cursor.Parse(...)` fallback for unsupported cursor grammars.
4. Add parser + generator + guard tests for deterministic lowering and fallback preservation.

Acceptance:

- Standard cursor literals avoid `Cursor.Parse(...)` in generated code.
- Unsupported cursor literal forms retain `Cursor.Parse(...)` fallback.
- Parser/generator/guard tests pass.

## Phase 8: Deterministic KeyGesture Lowering

Deliverables:

1. Add compiler parser semantics for canonical `KeyGesture` literals aligned with Avalonia tokenization:
   - `Ctrl+Shift+A`
   - `Cmd+F10`
   - `Ctrl++` (`+` synonym key)
   - modifier-only shape (`Alt`) mapped to `Key.None` + modifier flags
2. Emit deterministic `new KeyGesture(Key, KeyModifiers)` expressions for supported literals.
3. Keep `KeyGesture.Parse(...)` fallback for unsupported literal shapes.
4. Add parser + generator + guard tests for deterministic lowering and fallback preservation.

Acceptance:

- Canonical key-gesture literals avoid `KeyGesture.Parse(...)` in generated code.
- Unsupported key-gesture literal forms retain `KeyGesture.Parse(...)` fallback.
- Parser/generator/guard tests pass.

## Phase 9: Residual Parse Fallback Governance

Deliverables:

1. Add guard tests that enumerate explicit binder-emitted `global::*.Parse(...)` calls and enforce an allowlist.
2. Keep generic static parse fallback path (`TryConvertByStaticParseMethod`) explicitly verified to prevent accidental removal.
3. Ensure any future explicit parse additions require intentional review and inventory update.

Acceptance:

- Guard tests fail when new explicit parse emission types are introduced without allowlist update.
- Existing intentional explicit parse emission remains covered.

## Risks / Constraints

1. Some literal grammars are intentionally broad in Avalonia runtime parsers; compiler intrinsic conversion should only lower forms with exact deterministic semantics.
2. Stub-based generator tests must remain compatible; intrinsic conversion must not require constructors absent from test stubs when fallback parse is the only available contract.
3. Netstandard2.0 constraints apply (avoid APIs unavailable in generator stack target).

## Validation Matrix

1. `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlAvaloniaValueLiteralSemantics"`
2. `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~AvaloniaXamlSourceGeneratorTests"`
3. Full suite run after integration.

## Status (2026-03-01)

1. Phase 1 implemented.
2. Phase 2 implemented.
3. Phase 3 implemented:
   - compiler emits typed `FontFeatureCollection` expressions;
   - binder no longer emits runtime helper calls for font feature/family conversions;
   - guard coverage added to prevent regression.
4. Phase 4 implemented:
   - added residual `.Parse` sweep classification and rationale in inventory;
   - added centralized `DateTime` literal semantics with compiler-emitted `DateTime.FromBinary(...)`;
   - expanded guard coverage to enforce intrinsic non-fallback behavior for covered Avalonia types.
5. Phase 5 implemented:
   - brush path now emits deterministic `SolidColorBrush` construction for color literals;
   - non-color brush literals continue to use `Brush.Parse(...)` fallback;
   - regression tests added for deterministic lowering and fallback preservation.
6. Phase 6 implemented:
   - added centralized `XamlAvaloniaTransformLiteralSemantics` parser for canonical transform grammars;
   - binder now emits deterministic builder-based `TransformOperations` expressions for supported forms;
   - unsupported shapes retain `TransformOperations.Parse(...)` fallback;
   - parser/generator guard tests added.
7. Phase 7 implemented:
   - added centralized `XamlAvaloniaCursorLiteralSemantics` parser for deterministic standard-cursor tokens;
   - binder now emits deterministic `new Cursor(StandardCursorType.Member)` expressions for supported forms;
   - unsupported cursor shapes retain `Cursor.Parse(...)` fallback;
   - parser/generator guard tests added.
8. Phase 8 implemented:
   - added centralized `XamlAvaloniaKeyGestureLiteralSemantics` parser for canonical key/modifier tokenization;
   - binder now emits deterministic `new KeyGesture(Key, KeyModifiers)` expressions for supported forms;
   - unsupported key-gesture literals retain `KeyGesture.Parse(...)` fallback;
   - parser/generator/guard tests added.
9. Phase 9 implemented:
   - added parse-fallback governance tests enforcing explicit parse allowlist in binder emission paths;
   - added guard coverage that generic static parse fallback hook remains present and intentional.
