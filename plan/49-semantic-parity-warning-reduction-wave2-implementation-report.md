# Semantic/Parity Warning Reduction - Wave 2 Implementation Report

Date: 2026-02-20

## Scope Executed

Executed `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/48-semantic-parity-warning-reduction-wave2.md`.

Primary targets:
- eliminate generated compile blocker (`CS0030` casts from binding object-element paths)
- remove attached-property false positives in style/control-theme setter validation
- remove design-time property-element false positives (`Design.PreviewWith`)
- restore property-element collection/add behavior for multi-value collection properties

## Baseline vs Result

Build target:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj`

Baseline (before this wave):
- `448` warnings
- `7` errors (`CS0030`)
- top buckets included: `AXSG0301` (168), `AXSG0101` (100), `AXSG0103` (76, including Row/ColumnDefinitions), `AXSG0501` (182)

After implementation:
- `278` warnings
- `0` errors
- top buckets:
  - `AXSG0501`: 182
  - `AXSG0002`: 148
  - `AXSG0102`: 76
  - `AXSG0110`: 58
  - `AXSG0103`: 42 (only content-attachment shape warnings)
  - `AXSG0100`: 26

Notable reductions:
- `AXSG0301`: `168 -> 0`
- `AXSG0303`: `40 -> 0`
- `AXSG0101`: `100 -> 0`
- `AXSG0103`: `76 -> 42` (Row/ColumnDefinitions cardinality warnings eliminated)
- `CS0030` compile errors: `7 -> 0`

## Implemented Changes

## 1) Property-element parsing/binding parity

Files:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Changes:
1. Parser now preserves owner-qualified property-element names (`Owner.Property`) instead of stripping owner token.
2. Binder now ignores design-time property tokens (`Design.*`) in runtime binding path.
3. Property-element flow now:
   - binds object values early,
   - resolves attached property elements (`Owner.Property`) when possible,
   - prefers collection-add and Avalonia-property assignment before scalar CLR setter fallback.
4. Collection property elements with multiple values no longer fail early on scalar-setter cardinality checks.

Impact:
- fixed invalid object-element binding casts through correct Avalonia-property routing
- removed `PreviewWith` false positives
- removed Row/ColumnDefinitions false cardinality warnings

## 2) Style/ControlTheme attached setter parity

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Changes:
1. Setter property token resolution now honors explicit owner-qualified tokens first (`Grid.Row`, `ScrollViewer.VerticalScrollBarVisibility`, etc.).
2. If explicit owner resolves to an Avalonia property field, binder suppresses target CLR-property missing warning for that setter.
3. Setter value-type inference now uses resolved Avalonia property field type when CLR property is absent.
4. Duplicate detection key now uses Avalonia property identity (owner+field) when available.

Impact:
- eliminated attached-property false positives in `AXSG0301`/`AXSG0303` buckets.

## 3) Emitter binding object-element assignment safety

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

Changes:
1. Binding expression detection expanded to include `MultiBinding` and markup-extension binding return paths.
2. Added binding-object-node detection for property-element object values.
3. For Avalonia-property property-element assignments, emitter now uses indexer-binding assignment when assigned node is binding-like, both in normal and top-down initialization paths.

Impact:
- removed compile-time cast failures (`Binding/MultiBinding` -> scalar CLR property types) in generated files.

## 4) Regression test coverage

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added tests:
1. `Emits_Indexer_Assignment_For_Binding_PropertyElement_On_Avalonia_Property`
2. `Treats_MultiValue_Collection_PropertyElement_As_Collection_Add`
3. `Resolves_Attached_Style_Setter_Property_Token`
4. `Ignores_Design_PreviewWith_PropertyElement_During_Runtime_Binding`

All `AvaloniaXamlSourceGeneratorTests` pass (`132/132`).

## Validation Commands

1. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj -v minimal`
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter AvaloniaXamlSourceGeneratorTests -v minimal`
3. `dotnet clean /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj -v minimal`
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj -v minimal`

## Remaining Work (Next Wave)

1. `AXSG0501` volume: infer `ControlTemplate` target type from enclosing setter/theme/style context before emitting missing-target warning.
2. `AXSG0102` setter conversion parity for complex literal/value forms (`none`, transform functions, selector-literals used as setter values).
3. `AXSG0110/AXSG0111` policy tuning for theme bindings where runtime context intentionally lacks static `x:DataType`.
4. `AXSG0002` policy reclassification for classless theme dictionaries (likely informational rather than warning).
