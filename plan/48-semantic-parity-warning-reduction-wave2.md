# Semantic/Parity Warning Reduction Plan - Wave 2

Date: 2026-02-20

## Current Baseline (Fluent Sample)

Build target:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj`

Current output snapshot:
- `448` warnings
- `7` compile errors (`CS0030`) from generated CLR casts of `Binding/MultiBinding` object-element values.

Top warning buckets:
1. `AXSG0501` (182): ControlTemplate missing `TargetType` validation policy.
2. `AXSG0301` (168): style setter property lookup failures (mostly attached-property tokens like `Grid.Row`, `DockPanel.Dock`, `TextElement.Foreground`).
3. `AXSG0002` (148): classless resource/theme files (expected for theme dictionaries).
4. `AXSG0101` (100): unresolved property element `PreviewWith` (design-time property element path).
5. `AXSG0103` (76): property-element cardinality/content-attachment mismatches (notably `RowDefinitions/ColumnDefinitions`).
6. `AXSG0102` (76): remaining setter/value conversion gaps (policy + conversion edge cases).

## Root-Cause Groups

1. Property-element binding/AP precedence regression
- Settable CLR properties are handled before Avalonia-property resolution in property-element flow.
- This emits direct typed CLR assignments for object-element values, causing invalid casts (`Binding` -> scalar CLR type).

2. Property-element collection/add semantics ordering
- Collection-capable properties with setters (`RowDefinitions`, `ColumnDefinitions`) are treated as scalar settable properties first.
- Produces `AXSG0103` "requires exactly one object value" instead of collection-add behavior.

3. Attached-property parity in style/control-theme setters
- Setter token resolution normalizes `Owner.Property` to `Property` too early.
- Binder validates against target CLR type only, missing attached owners (`Grid`, `DockPanel`, `ScrollViewer`, `TextElement`, `Inline`, `TopLevel`).

4. Design-time property-element handling
- `Design.PreviewWith` enters normal runtime semantic binding and emits `AXSG0101`.
- Expected behavior is to ignore design-time-only members in runtime compilation.

## Implementation Plan

## WS1: Fix property-element precedence and compile blocker
1. Rework property-element binding order:
- evaluate collection-add path before scalar CLR setter path.
- resolve Avalonia property field before CLR direct-set fallback.
2. Ensure object-element values for Avalonia properties no longer emit direct CLR typed casts.
3. Keep diagnostics for truly unsupported cases only.

Exit criteria:
- `CS0030` generated compile errors eliminated.

## WS2: Add attached property-element support and design-time ignore
1. Preserve owner-qualified property element tokens from parser (`Owner.Property`), do not strip owner at parse stage.
2. Add binder support for owner-qualified attached property elements (e.g., `ToolTip.Tip`).
3. Skip design-only property elements (`Design.*`) in runtime binding pipeline.

Exit criteria:
- `PreviewWith` warning cluster removed.
- Attached property-element false positives reduced.

## WS3: Style/control-theme attached setter resolution parity
1. Resolve setter `Property` token using owner-qualified token first when present.
2. If owner-qualified Avalonia property resolves, treat as valid without CLR-property warning on target type.
3. Use resolved Avalonia property type for setter value conversion and metadata.

Exit criteria:
- Major reduction in `AXSG0301` + `AXSG0303` attached-property warnings.

## WS4: Validation + tests
1. Add/extend generator tests for:
- property-element binding object values on Avalonia properties (no direct CLR cast emission).
- collection property element multiple-value add semantics.
- style/control-theme attached setter property resolution.
- attached property-element (`ToolTip.Tip`) and design `Design.PreviewWith` suppression.
2. Rebuild Fluent sample and recapture warning histogram.

## Deferred (next wave)
1. `AXSG0501` target-type policy: infer control-template target from setter/theme context before warning.
2. Remaining `AXSG0102` conversion edge cases for complex setter values (transforms/selector-literal-in-setter fallback behavior).
3. Review `AXSG0002` policy for classless resource dictionaries (likely informational-by-design).
