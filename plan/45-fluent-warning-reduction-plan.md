# Fluent SourceGen Warning Reduction Plan (Targeted Implementation)

Date: 2026-02-20

## Context

`/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj` now builds with SourceGen backend, but clean build warning volume is high.

Baseline (`dotnet clean && dotnet build`, net10.0):

- `AXSG0100`: 2024
- `AXSG0102`: 1446
- `AXSG0300`: 428
- `CS8601`: 428
- `CS8605`: 404

## Root Cause Clusters

1. Type resolution misses short tokens in Avalonia XAML.
  - Selector targets like `ContentPresenter`, `Path`, `Ellipse`, `ToggleButton`, `Thumb`.
  - Resource element names and markup-extension object-element names (for example `StaticResource`).

2. Literal conversion gaps in semantic binder.
  - Plain string type tokens for `System.Type`/`TargetType`/`DataType` paths.
  - Nullable reference type display mismatch (`System.Uri?`) in URI conversion branch.

3. Selector binding path reuses type resolver but inherits its short-name limitations.
  - Causes both `AXSG0300` (selector invalid/unresolved target) and second-order `AXSG0102` for selector literals.

4. Generated C# nullability warnings in dynamic/late-bound resource paths.
  - Large `CS8601`/`CS8605` volume in generated files.

## Implementation Plan

## WS1: Type Resolver Coverage Expansion

1. Expand Avalonia default namespace candidate list in binder resolver to include:
   - `Avalonia.Controls.Primitives.`
   - `Avalonia.Controls.Presenters.`
   - `Avalonia.Controls.Shapes.`
   - `Avalonia.Markup.Xaml.MarkupExtensions.`
2. Update `ResolveTypeToken` fallback path to scan candidate namespaces (not only `Avalonia.Controls.`).
3. Add markup-extension suffix fallback in `ResolveTypeSymbol` for Avalonia default XML namespace:
   - Try `<TypeName>Extension` when direct type lookup fails.
4. Add intrinsic primitive fallback by token name (for unprefixed primitive usage such as `Double`).

Expected impact:
- Major reduction in `AXSG0300`.
- Significant reduction in `AXSG0100`.

## WS2: Conversion Improvements

1. Add `System.Type` conversion in `TryConvertValueExpression`:
   - Resolve type token via `ResolveTypeFromTypeExpression` and emit `typeof(...)`.
2. Make URI conversion robust for nullable reference display forms:
   - handle `System.Uri?` and fully qualified forms consistently.
3. Keep existing markup-extension conversion precedence unchanged.

Expected impact:
- Significant reduction in `AXSG0102` (`TargetType`, `DataType`, include `Source` literals).

## WS3: Generated Nullability Warning Reduction

1. Emit generated files under explicit nullability suppression boundary (generated-only scope).
2. Preserve runtime behavior; avoid changing semantic model or runtime conversion behavior.

Expected impact:
- Major reduction in `CS8601` and `CS8605`.

## WS4: Validation Loop

1. Re-run:
   - `dotnet clean /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj`
   - `dotnet build ... -v minimal`
2. Capture warning histogram by code.
3. Compare against baseline and document deltas.

## Non-Goals In This Wave

- Reclassification/suppression of AXSG diagnostics without behavioral improvements.
- Full parity completion for all remaining style/template edge-cases.

## Exit Criteria

1. Fluent sample still builds successfully with SourceGen backend.
2. Warning counts for at least 3 largest buckets materially reduced from baseline.
3. No new build errors introduced in samples or solution build.
