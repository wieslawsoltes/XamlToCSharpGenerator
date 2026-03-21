# PR Summary: AssignBinding CLR Properties and Binding-Local `DataType`

## Branch

- `codex/assignbinding-bindinglocal-datatype`

## Commit Breakdown

1. `9073e0c92` `Support binding-local DataType on assignable bindings`
2. `38855852b` `Use binding-local DataType in language service`
3. `fb23e1e84` `Handle x:Type DataType in object-node bindings`

## Problem Statement

AXSG handled Avalonia compiled bindings correctly for the common control/property cases, but it still missed an important Avalonia-specific pattern:

- a CLR property marked with `AssignBindingAttribute`
- whose type accepts an assigned binding object such as `BindingBase`
- receiving a compiled binding that provides its own `DataType`

In that shape, AXSG could incorrectly fall back to the parent ambient `x:DataType` and report semantic diagnostics such as `AXSG0110` or `AXSG0111` against the wrong source type. The same gap also affected editor features, because completion and navigation were using the ambient data type instead of the binding-local one.

There was a second gap in the object-node parser:

- object-node bindings could preserve `DataType` when written as an attribute
- but they did not fully handle property-element forms that used `<x:Type .../>`

That meant verbose compiled-binding forms like:

```xaml
<CompiledBinding>
  <CompiledBinding.Path>Rows</CompiledBinding.Path>
  <CompiledBinding.DataType>
    <x:Type TypeName="vm:MainVm" />
  </CompiledBinding.DataType>
</CompiledBinding>
```

could still lose the intended source type during semantic analysis.

## High-Level Solution

The change teaches AXSG to treat binding-local `DataType` as the authoritative source type whenever Avalonia markup semantics indicate the binding object itself is being assigned, rather than applied through an ambient binding target.

The implementation has three parts:

1. Preserve `DataType` in parsed binding models and use it during semantic binding.
2. Mirror the same source-type resolution in the language service.
3. Extend object-node parsing so `CompiledBinding.DataType` also works when written as a property element containing `x:Type`.

## Detailed Changes

### 1. Core binding model and semantic binder

Files:

- `src/XamlToCSharpGenerator.Core/Models/BindingEventMarkupModels.cs`
- `src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`

Key changes:

- Added `DataType` to `BindingMarkup` so the parsed binding model no longer drops binding-local type information.
- Updated `BindingEventMarkupParser` to parse, normalize, and preserve `DataType` through all binding rewrites.
- Updated semantic source-type resolution to prefer `bindingMarkup.DataType` before falling back to ambient `x:DataType`.
- Added `ResolveBindingMarkupDataType(...)` to centralize type resolution from binding-local type expressions.
- Extended emitted binding initializers to include `DataType` when the target binding type exposes that property.
- Expanded assign-binding property detection so CLR properties typed as `BindingBase`, `IBinding`, or `IBinding2` are all treated as valid binding-holder targets.
- Added the canonical object-node argument mapping for `DataType`, allowing object-node compiled bindings to feed the same binding model.
- Updated internal `BindingMarkup` construction sites to pass the new `dataType` argument explicitly.

Behavioral effect:

- AXSG now resolves `{CompiledBinding Name, DataType={x:Type vm:RowVm}}` against `RowVm` even when the surrounding element has a different ambient `x:DataType`.
- This now works for assign-binding CLR properties, not just standard Avalonia property-binding paths.

### 2. Language service parity

Files:

- `src/XamlToCSharpGenerator.LanguageService/Completion/XamlSemanticSourceTypeResolver.cs`
- `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlBindingNavigationService.cs`

Key changes:

- Added binding-local `DataType` resolution to completion source-type inference.
- Added the same logic to definition/navigation lookup.
- Reused element-local prefix map resolution so `vm:Foo` and similar type expressions resolve the same way as they do in generator analysis.

Behavioral effect:

- completion inside binding paths prefers the binding-local source type
- go-to-definition / navigation resolves binding members against the correct type
- editor behavior now matches generator/runtime semantics for this Avalonia pattern

### 3. Object-node `x:Type` property-element support

Files:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Key changes:

- Extended `TryGetSingleBindingObjectNodeArgumentValue(...)` so it can extract values from an `x:Type` object node, not only raw text or markup-extension argument forms.
- Added `TryExtractTypeExpressionFromXamlTypeNode(...)` to support:
  - `TypeName="vm:MainVm"`
  - `Type="vm:MainVm"`
  - property-element forms
  - constructor/text-content fallback
  - generic type argument reconstruction when the `x:Type` node carries type arguments

Behavioral effect:

- object-node compiled bindings now keep `DataType` when it is expressed via property-element `x:Type`
- inherited data-type flows such as `ItemsSource` to `ItemTemplate` now work for this verbose binding form as well

## Test Coverage Added

### Generator tests

File:

- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added coverage for:

- assign-binding CLR properties typed as `BindingBase` with binding-local `DataType`
- both attribute and object-node compiled-binding forms for assign-binding CLR properties
- object-element `ItemsSource` compiled binding with `CompiledBinding.DataType` expressed as property-element `<x:Type .../>`

The tests assert:

- no `AXSG0110` / `AXSG0111` diagnostics are produced
- the generated descriptors use the expected source type
- the generated code still emits assign-binding object assignments

### Language service tests

File:

- `tests/XamlToCSharpGenerator.Tests/LanguageService/XamlLanguageServiceEngineTests.cs`

Added coverage for:

- completion preferring binding-local `DataType` over ambient `x:DataType`

The test asserts that:

- members from the local binding type are offered
- unrelated members from the ambient type are not offered

## Validation

Executed on the feature branch:

```bash
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~Uses_BindingLocal_DataType_For_AssignBinding_Clr_Property_CompiledBindings|FullyQualifiedName~Infers_ItemTemplate_DataType_From_ObjectElement_ItemsSource_Binding_PropertyElement_DataType_XamlType|FullyQualifiedName~Completion_InBindingPathContext_Prefers_BindingLocal_DataType" -v minimal
```

Result:

- Passed: `3`
- Failed: `0`
- Skipped: `0`

Notes:

- The test run still surfaces existing nullable warnings from unrelated sample and Avalonia binder code paths.
- No new failing diagnostics or regressions were observed in the focused slice relevant to this change.

## Reviewer Guide

Suggested review order:

1. `BindingEventMarkupModels.cs`
2. `BindingEventMarkupParser.cs`
3. `AvaloniaSemanticBinder.BindingSemantics.cs`
4. `XamlSemanticSourceTypeResolver.cs`
5. `XamlBindingNavigationService.cs`
6. `AvaloniaSemanticBinder.ObjectNodeBinding.cs`
7. new regression tests

Key things to verify:

- binding-local `DataType` wins over ambient `x:DataType` only when it is explicitly present
- assign-binding CLR properties typed as `BindingBase` are treated as valid binding-holder properties
- object-node `CompiledBinding.DataType` handles both attribute and property-element `x:Type` forms
- language-service source-type inference matches generator behavior

## Expected User Impact

After this change, Avalonia authors using:

- `AssignBindingAttribute`
- `BindingBase`-typed CLR properties
- compiled bindings with local `DataType`

should get correct AXSG generation, completion, and navigation results without false ambient-type diagnostics. This brings AXSG closer to actual Avalonia binding semantics for assigned binding objects rather than applied bindings.
