# Phase A Baseline: String-Literal Semantic Inventory (2026-02-26)

## Scope

This baseline tracks direct semantic metadata probes shaped as:

`GetTypeByMetadataName("...")`

in `src/` C# sources.

Purpose:

1. Prevent spread of direct string-literal semantic probes into new files.
2. Keep a measurable baseline for de-hack migration phases.
3. Classify existing literals into semantic-contract vs data-payload buckets.

## Classification

Current classification rules:

1. `Avalonia.*` => `semantic-contract.framework`
2. `System.*` => `semantic-contract.bcl`
3. other => `unknown` (must be zero)
4. `data-payload` => not applicable for this probe shape (zero)

## Snapshot Metrics

1. Total direct `GetTypeByMetadataName("...")` literals: `46`
2. `semantic-contract.framework`: `36`
3. `semantic-contract.bcl`: `10`
4. `data-payload`: `0`
5. `unknown`: `0`

## Hotspot Files (Baseline Ceiling)

| Count | File |
|---:|---|
| 31 | `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs` |
| 7 | `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.NodeTypeResolution.cs` |
| 2 | `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.MarkupHelpers.cs` |
| 2 | `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs` |
| 2 | `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs` |
| 1 | `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.SelectorPropertyReferences.cs` |
| 1 | `src/XamlToCSharpGenerator.Avalonia/Binding/Services/NameScopeRegistrationSemanticsService.cs` |

## Guard Policy

1. New direct `GetTypeByMetadataName("...")` usage in unapproved files: fail tests.
2. Baseline counts are treated as ceilings; increases fail tests.
3. Reductions are allowed and expected during de-hack migration.

## Test Linkage

Enforced by:

`tests/XamlToCSharpGenerator.Tests/Generator/StringLiteralSemanticInventoryTests.cs`

