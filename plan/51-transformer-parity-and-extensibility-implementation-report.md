# Transformer Parity + Extensibility Implementation Report (2026-02-20)

## Scope Executed

Implemented end-to-end transformer extensibility and parity wiring for custom type/property transforms across the SourceGen pipeline.

## Delivered Changes

### 1. Core Contract

- Added transform configuration model:
  - `XamlTransformConfiguration`
  - `XamlTypeAliasRule`
  - `XamlPropertyAliasRule`
- Updated semantic binder abstraction:
  - `IXamlSemanticBinder.Bind(..., XamlTransformConfiguration transformConfiguration)`

### 2. Build/MSBuild Wiring

- Added build property:
  - `AvaloniaSourceGenTransformRules`
- Added transform rule item support:
  - `AvaloniaSourceGenTransformRule`
- Added AdditionalFiles projection with:
  - `SourceItemGroup="AvaloniaSourceGenTransformRule"`
- Added watch/deduplication flow for transform-rule inputs in targets.

### 3. Generator Pipeline

- Added transform rule parser (`TransformRulesParser`) for JSON rule files.
- Added incremental discovery path for rule files via AdditionalFiles metadata.
- Added merge/override handling with duplicate diagnostics.
- Added diagnostic mapping/reporting for `AXSG0900`..`AXSG0903`.
- Binder invocation now passes merged transform configuration.

### 4. Binder Pipeline

- Added early custom-transform pass:
  - `AXSG-P000-BindCustomTransforms`.
- Added assembly-attribute ingestion:
  - `SourceGenXamlTypeAliasAttribute`
  - `SourceGenXamlPropertyAliasAttribute`
  - `SourceGenXamlAvaloniaPropertyAliasAttribute`
- Added type alias resolution into:
  - `ResolveTypeToken`,
  - `ResolveTypeSymbol`.
- Added property alias resolution into:
  - object property assignment path,
  - property-element path,
  - style setter path,
  - control-theme setter path.
- Added explicit Avalonia owner+field handling for aliased attached/Avalonia-property paths.

### 5. Runtime Attribute Surface

- Added runtime attribute definitions for assembly-level alias registration in:
  - `SourceGenXamlTransformAttributes.cs`.

### 6. Emission Safety Fix

- Fixed binding-expression detection for `ProvideMarkupExtension(...)`:
  - only treats it as binding-like when cast to binding types.
- Prevents invalid `IndexerDescriptor` assignment for non-binding markup-extension results.

### 7. Test Coverage

- Extended test analyzer-config provider to support per-AdditionalFile metadata.
- Added generator tests:
  - transform rule type/property aliases,
  - assembly attribute aliases,
  - invalid transform rule diagnostics.
- Restored/kept existing corpus and runtime differential tests green.

## Validation

- Command run:
  - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal`
- Result:
  - Passed: 236
  - Failed: 0
  - Skipped: 1

## Follow-Up Opportunities

1. Add rule-file schema versioning + optional strict schema validation mode.
2. Add transform-trace diagnostics that include winning alias rule source per rewritten token.
3. Add dedicated differential fixtures for wildcard target aliases and attached-field aliases.
