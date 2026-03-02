# Avalonia Parse Inventory (Compiler Coverage)

Date: 2026-03-01

## Source Baseline

Inventory source: `Avalonia/src` static `Parse(string...)` methods and XAML intrinsic conversion behavior in Avalonia XAML compiler integration.

## Coverage Matrix

| Type | Avalonia parse entry | Compiler coverage status |
| --- | --- | --- |
| `System.DateTime` | `DateTime.Parse(..., RoundtripKind)` | Implemented compiler-time roundtrip parse + emitted `DateTime.FromBinary(...)` |
| `System.TimeSpan` | intrinsic parse | Implemented intrinsic conversion (`FromTicks`) |
| `Avalonia.Thickness` | `Thickness.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.CornerRadius` | `CornerRadius.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Point` | `Point.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Vector` | `Vector.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Size` | `Size.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Rect` | `Rect.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Matrix` | `Matrix.Parse(string)` | Implemented intrinsic constructor emission (6/9 component forms) |
| `Avalonia.Vector3D` | `Vector3D.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.PixelPoint` | `PixelPoint.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.PixelSize` | `PixelSize.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.PixelRect` | `PixelRect.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Controls.GridLength` | `GridLength.Parse(string)` | Implemented intrinsic conversion (`Auto`/`Star`/`Pixel`) |
| `Avalonia.Controls.RowDefinition` | grid-length based conversion | Implemented intrinsic conversion via `GridLength` |
| `Avalonia.Controls.ColumnDefinition` | grid-length based conversion | Implemented intrinsic conversion via `GridLength` |
| `Avalonia.RelativePoint` | `RelativePoint.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.RelativeScalar` | `RelativeScalar.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.RelativeRect` | `RelativeRect.Parse(string)` | Implemented intrinsic constructor emission |
| `Avalonia.Media.Color` | `Color.Parse/TryParse` | Implemented intrinsic hex color conversion + named colors (`Colors.*`) |
| `Avalonia.Media.FontFeatureCollection` | tokenized collection + `FontFeature.Parse` | Implemented compiler tokenization + typed collection emission |
| `Avalonia.Media.FontFamily` | `FontFamily.Parse(string[, Uri])` | Implemented compiler parse-call emission (with base URI when available) |
| `Avalonia.Media.IBrush` / `Brush` | `Brush.Parse(string)` | Deterministic color-literal lowering to `SolidColorBrush(Color)`; parse retained for non-color grammars |
| `Avalonia.Media.Transformation.TransformOperations` | `TransformOperations.Parse(string)` | Partial deterministic lowering + parse fallback |
| `Avalonia.Media.Geometry`/`PathGeometry`/`StreamGeometry` | parse methods | Currently retained generic static parse fallback |
| `Avalonia.Media.TextDecorationCollection` | parse/static forms | Static-property conversion path already available |
| `Avalonia.Media.TextTrimming` | parse/static forms | Static-property conversion path already available |
| `Avalonia.Input.Cursor` | `Cursor.Parse(string)` | Deterministic `StandardCursorType` lowering + parse fallback |
| `Avalonia.Input.KeyGesture` | `KeyGesture.Parse(string)` | Deterministic canonical key/modifier lowering + parse fallback |
| `Avalonia.Controls.RowDefinitions` | `RowDefinitions.Parse(string)` | Covered through collection + row-definition literal conversion |
| `Avalonia.Controls.ColumnDefinitions` | `ColumnDefinitions.Parse(string)` | Covered through collection + column-definition literal conversion |

## Notes

1. The compiler now performs deterministic, parser-backed lowering for intrinsic numeric/layout/value types that were previously left to generated `.Parse("...")` strings.
2. Remaining `.Parse` usage is limited to types whose grammar is broader/behavioral and not yet represented as deterministic intrinsic constructors in this phase.
3. Runtime helpers for font feature collection/font family conversion are no longer required by newly generated code paths after this slice.
4. Parse fallback governance is enforced by tests that constrain explicit binder-emitted `global::*.Parse(...)` calls to an intentional allowlist.

## Residual `.Parse` Classification (Phase 4)

| Location | Parse form | Classification | Rationale |
| --- | --- | --- | --- |
| `AvaloniaSemanticBinder.BindingSemantics.TryConvertAvaloniaBrushExpression` | `Brush.Parse(string)` | Partial deterministic lowering + runtime fallback | Color-only literals lower to `SolidColorBrush(Color)`; non-color grammars intentionally remain runtime parse. |
| `AvaloniaSemanticBinder.BindingSemantics.TryConvertAvaloniaTransformExpression` | `TransformOperations.Parse(string)` | Partial deterministic lowering + runtime fallback | Canonical transform function-list literals lower via builder emission; unsupported/invalid shapes intentionally retain runtime parse fallback. |
| `AvaloniaSemanticBinder.BindingSemantics.TryConvertAvaloniaCursorExpression` | `Cursor.Parse(string)` | Partial deterministic lowering + runtime fallback | Standard-cursor literals lower to `new Cursor(StandardCursorType.Member)`; unsupported cursor grammars intentionally retain runtime parse fallback. |
| `AvaloniaSemanticBinder.BindingSemantics.TryConvertAvaloniaKeyGestureExpression` | `KeyGesture.Parse(string)` | Partial deterministic lowering + runtime fallback | Canonical key-gesture literals lower to `new KeyGesture(Key, KeyModifiers)`; unsupported key/modifier shapes intentionally retain runtime parse fallback. |
| `AvaloniaSemanticBinder.BindingSemantics.TryConvertFontFeatureCollectionLiteralExpression` | `FontFeature.Parse(string)` | Intentional typed parse | Collection tokenization is compiler-side; each feature token still resolves via framework parser contract. |
| `AvaloniaSemanticBinder.BindingSemantics.TryConvertFontFamilyLiteralExpression` | `FontFamily.Parse(string[, Uri])` | Intentional typed parse | Parse overload selection and base-uri handling are contract-based and deterministic at compile time. |
| `AvaloniaSemanticBinder.MarkupHelpers.TryConvertByStaticParseMethod` | `T.Parse(...)` fallback | Intentional generic fallback | Required extensibility path for framework/custom parse-capable types without intrinsic lowering. |
