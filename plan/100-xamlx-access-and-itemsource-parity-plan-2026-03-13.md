# XamlX Access Rules and `InheritDataTypeFromItems` Parity Plan (2026-03-13)

## Scope
Implement two parity fixes in the AXSG Avalonia backend:

1. Compiled-binding member access parity versus Avalonia XamlIl/XamlX for non-public properties and methods.
2. Data-type inference parity for properties annotated with `InheritDataTypeFromItemsAttribute`, including `ItemTemplate`-style template scopes and binding properties such as `DisplayMemberBinding`.

Primary upstream references used for this analysis:
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/XamlIlBindingPathHelper.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlDataContextTypeTransformer.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Base/Metadata/InheritDataTypeFromItemsAttribute.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Controls/ItemsControl.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/tests/Avalonia.Markup.Xaml.UnitTests/MarkupExtensions/CompiledBindingExtensionTests.cs`
- `/Users/wieslawsoltes/GitHub/XamlX/src/XamlX/TypeSystem/TypeSystem.cs`
- `/Users/wieslawsoltes/GitHub/XamlX/src/XamlX/IL/SreTypeSystem.cs`

## Current AXSG behavior

### Access checks
Current AXSG compiled-binding path binding uses Roslyn accessibility checks against the generated view class:
- `FindAccessibleProperty(...)`
- `FindAccessibleParameterlessMethod(...)`
- `ResolveMethodCommandCandidates(...)`
- `TryResolveMethodInvocation(...)`
- `GetGeneratedCodeAccessibilityWithinSymbol(...)`

Those checks live in:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`

Result:
- `public` works.
- `internal` works only when Roslyn says the generated class can see it.
- `protected` and `private` are rejected up front with `AXSG0111`.

### Template/binding item-scope inference
Current AXSG resolves `x:DataType`, template `DataType`, and explicit `DataContext` bindings, but it does not have an equivalent of Avalonia's `AvaloniaXamlIlDataContextTypeTransformer`.

Result:
- `ItemTemplate` / custom template properties annotated with `InheritDataTypeFromItemsAttribute` do not inherit the presented item type from `ItemsSource`-like properties.
- binding properties annotated with `InheritDataTypeFromItemsAttribute` do not switch their binding scope to the presented item type.

## Upstream behavior

### 1) XamlX/Avalonia compiled-binding access behavior
Avalonia compiled-binding path resolution uses symbol lookup without CLR accessibility filtering:
- `XamlIlBindingPathHelper.TransformBindingPath(...)` resolves CLR properties through `GetAllDefinedProperties(...)`
- methods are resolved through `GetAllDefinedMethods(...)`
- `XamlX` type systems expose public and non-public members:
  - `/Users/wieslawsoltes/GitHub/XamlX/src/XamlX/IL/SreTypeSystem.cs`
  - property/method lists are built with `BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly`

Important implication:
- semantic binding does not reject `private` / `protected` / `internal` members during path resolution.

Emitter/runtime side:
- Avalonia/XamlX later emits IL/runtime handles for the resolved getter/method rather than C# member-access syntax.
- That means XamlIl is not constrained by C# source accessibility in the same way AXSG is.

### 2) `InheritDataTypeFromItemsAttribute`
Avalonia uses `InheritDataTypeFromItemsAttribute` on binding/template-bearing CLR properties, for example:
- `ItemsControl.DisplayMemberBinding`
- `ItemsControl.ItemTemplate`
- `ComboBox.SelectionBoxItemTemplate`
- `SelectingItemsControl.SelectedValueBinding`

The transformer:
- finds the parent property assignment node,
- reads the attribute from the parent CLR property,
- resolves the ancestor object specified by `AncestorType` if present,
- finds the ancestor items property named by `AncestorItemsProperty`,
- if the items property is a compiled binding, resolves its result type through `XamlIlBindingPathHelper.UpdateCompiledBindingExtension(...)`,
- extracts `IEnumerable<T>` item type,
- stamps the inferred data-context metadata on the current node.

This behavior is covered upstream by tests such as:
- `InfersDataTypeFromParentDataGridItemsTypeInCaseOfControlInheritance`
- `InfersDataTemplateTypeFromParentDataGridItemsType`

## Constraints specific to AXSG

### Non-negotiable repo constraints
- No reflection in emitted/runtime production paths.
- Preserve AOT/trimming compatibility.
- Fix parity at the semantic/emission layer, not with runtime fallback hacks.

### Observed target-framework constraint
Local probe results:
- `UnsafeAccessor` works on `net8.0`.
- `UnsafeAccessor` works on `net10.0`.
- `UnsafeAccessor` is not available on stock `net6.0`.
- defining a local shim attribute on `net6.0` still fails at runtime with `TypeLoadException` because the runtime has no implementation.

Conclusion:
- Full `private` / `protected` parity is implementable for compilations that expose `System.Runtime.CompilerServices.UnsafeAccessorAttribute`.
- It is not implementable on `net6.0` without violating the repo's no-reflection rule.

## Implementation design

### A. Non-public compiled-binding access
Add a semantic/accessor plan for inaccessible instance members:

1. Keep normal Roslyn accessibility checks for direct C# access.
2. If direct C# access fails:
   - if `UnsafeAccessorAttribute` is available in the target compilation, emit an AXSG unsafe-accessor helper plan instead of failing;
   - otherwise keep an actionable diagnostic explaining that the member is not source-accessible on this target.
3. Support:
   - property getters,
   - parameterless methods,
   - method invocations with arguments,
   - method-as-command accessors.

Planned AXSG representation:
- add a resolved unsafe-accessor model carrying:
  - generated helper method name,
  - `UnsafeAccessor` target member name,
  - declaring type,
  - return type,
  - parameter types.
- store these on the resolved view model so the emitter can generate the helper declarations once per unique signature.

Planned generated code shape:
- emit `[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "...")]` helpers in the generated partial view class.
- compiled-binding access expressions call those helpers instead of direct `source.Member` or `source.Method(...)` syntax when needed.

Example target shape:

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Context")]
private static extern global::Demo.ViewModels.WizardContext __AXSG_UA_12345678(global::Demo.ViewModels.WizardStepViewModelBase value);
```

### B. `InheritDataTypeFromItems` inference
Add a binder-side scope chain model for object nodes so inference can inspect:
- current node,
- current node type,
- current node data type,
- ancestor object nodes/types/data types,
- the property name on the parent object that produced the current child node.

Use that scope model in two places:

1. `ResolveNodeDataType(...)`
Purpose:
- infer the current node's `x:DataType` when the parent property is annotated with `InheritDataTypeFromItemsAttribute`.

2. `ResolveAssignmentBindingDataType(...)`
Purpose:
- infer the binding source type for current-node properties such as `DisplayMemberBinding` or custom `[AssignBinding]` binding properties annotated with `InheritDataTypeFromItemsAttribute`.

Inference algorithm:
1. Resolve the annotated CLR property.
2. Read `AncestorItemsProperty` and optional `AncestorType`.
3. Find the effective ancestor object in the current binder scope chain.
4. Find the ancestor items property assignment/property element.
5. Infer the collection type:
   - if the items property is a compiled-binding-capable binding (`CompiledBinding` or `Binding` under compile-bindings mode), resolve its result type through the existing compiled-binding semantic path;
   - otherwise, if a direct object type is available, use that type.
6. Extract `IEnumerable<T>` / collection item type.
7. Use the inferred item type as the node/assignment binding data type.

## Files to change

### Core models
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedViewModel.cs`
- new resolved unsafe-accessor model file under:
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/`

### Avalonia binder
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs`

### Avalonia emitter
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

### Tests
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

## Test plan

Add or update generator tests for:

1. `protected` property compiled binding now succeeds on `net10.0` and emits an unsafe-accessor helper.
2. `private` property compiled binding now succeeds on `net10.0` and emits an unsafe-accessor helper.
3. `internal` property compiled binding remains valid.
4. non-public method compiled binding succeeds:
   - parameterless method path,
   - method invocation with arguments,
   - method-to-command path.
5. `ItemTemplate` infers item type from `ItemsSource` via `InheritDataTypeFromItemsAttribute`.
6. custom ancestor-type inference works for `[InheritDataTypeFromItems(nameof(...), AncestorType = typeof(...))]` on:
   - binding-bearing properties,
   - template-bearing properties.

## Verification plan

1. Run targeted generator tests covering the new access and inference cases.
2. Run the full `XamlToCSharpGenerator.Tests` project.
3. Build the solution or, at minimum, the affected generator/emitter/test projects.

## Expected outcome

After the change:
- AXSG will match Avalonia/XamlX item-scope inference for `InheritDataTypeFromItemsAttribute`.
- AXSG will stop rejecting non-public compiled-binding members on `net8.0+` / `net10.0` when a source-generated unsafe accessor can legally bridge the gap without reflection.
- AXSG will keep a clear diagnostic on targets that cannot support that bridge, rather than silently changing semantics.

## Implementation status

Completed on 2026-03-13.

### Implemented changes

1. Non-public compiled-binding access parity
   - added a resolved unsafe-accessor model and plumbed it through binder output,
   - taught compiled-binding property, parameterless-method, method-invocation, and method-command resolution to emit `UnsafeAccessor` helpers when direct C# access is illegal but the target compilation supports `UnsafeAccessor`,
   - kept direct source-member access for normally accessible members,
   - preserved clear diagnostics when the bridge is unavailable.

2. `InheritDataTypeFromItemsAttribute` parity
   - added binder scope-chain tracking so child scopes can inspect parent property context and ancestors,
   - implemented current-node template/item-scope inference from annotated parent properties,
   - implemented binding-property inference from annotated properties such as `DisplayMemberBinding`-style members,
   - implemented `AncestorType` traversal so column/template members can inherit item type from ancestor controls.

3. Binding precedence fix discovered during validation
   - restored property-first binding semantics before method-command fallback,
   - prevented an inaccessible or hidden property from silently falling through to a same-name method command,
   - revalidated the existing `ICommand` precedence behavior.

### Files changed

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedUnsafeAccessorDefinition.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedViewModel.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ConstructionConditions.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.NoUi/Binding/NoUiSemanticBinder.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

## Verification results

Validated with:

1. `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj -m:1 /nodeReuse:false --disable-build-servers -clp:ErrorsOnly`
2. `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlToCSharpGenerator.Tests.Generator.AvaloniaXamlSourceGeneratorTests" -m:1 /nodeReuse:false --disable-build-servers -clp:ErrorsOnly`

Relevant new coverage includes:

- protected/private/internal property compiled bindings,
- protected/private/internal method access across parameterless methods, explicit invocation, and method-command fallback,
- `ItemTemplate` item-type inference from `ItemsSource`,
- ancestor-based `InheritDataTypeFromItemsAttribute` inference for binding and template properties.
