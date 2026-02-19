# WS7.1/WS7.2/WS7.3 Parity Progress Report (Parent Stack + Service Context)

## Scope completed in this pass
- Full deferred parent-stack/service-provider parity improvements.
- ProvideValue service-context parity improvements for markup-extension paths.
- Parent-stack runtime parity improvements with cache-equivalent behavior.
- Differential parity fixture expansion using Avalonia `BasicTests`-style scenarios.

## Implemented changes

### 1. Deferred parent-stack/service-provider parity
Updated:
- `src/XamlToCSharpGenerator.Runtime/SourceGenDeferredServiceProviderFactory.cs`

Key changes:
- Deferred template provider now captures upstream parent-stack provider.
- Deferred provider `Parents` now:
  - emits deferred-captured resource nodes first,
  - appends upstream parent stack with reference-based de-duplication.
- Added thread-static last-stack cache for filtered resource node snapshots (XamlIl cache-equivalent behavior).

### 2. ProvideValue service-context parity (IProvideValueTarget/IRootObjectProvider/IUriContext)
Updated:
- `src/XamlToCSharpGenerator.Core/Models/ResolvedPropertyAssignment.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
- `src/XamlToCSharpGenerator.Runtime/SourceGenMarkupExtensionRuntime.cs`
- `src/XamlToCSharpGenerator.Runtime/SourceGenProvideValueTargetPropertyFactory.cs`

Key changes:
- Added CLR property metadata to bound assignments (`ClrPropertyOwnerTypeName`, `ClrPropertyTypeName`).
- Emitter now builds `IProvideValueTarget.TargetProperty` for CLR assignments via generated writable `IPropertyInfo` descriptors:
  - `SourceGenProvideValueTargetPropertyFactory.CreateWritable<TTarget, TValue>(...)`.
- Markup-context expansion now receives real property descriptors (not `null`) for CLR markup-extension assignments.
- `TargetProperty` in runtime service context no longer uses `string.Empty`; now uses `AvaloniaProperty.UnsetValue` when absent.

### 3. DynamicResource parity path stabilization
Updated:
- `src/XamlToCSharpGenerator.Runtime/SourceGenMarkupExtensionRuntime.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

Key changes:
- `ProvideDynamicResource` now returns `Avalonia.Data.IBinding?`.
- DynamicResource conversion no longer force-casts to target CLR property type in binder.
- Emitter recognizes dynamic-resource runtime calls as binding-like expressions.
- Binder now prefers AvaloniaProperty assignment path for markup-extension values when Avalonia property metadata exists, enabling proper binding-style emission for DynamicResource.

### 4. Differential fixture expansion (Avalonia BasicTests-style edge cases)
Updated:
- `tests/XamlToCSharpGenerator.Tests/Build/DifferentialFeatureCorpusTests.cs`

Added fixtures:
- `namescope-reference-basic` (`ElementName` namescope resolution shape).
- `deferred-template-resource-basic` (deferred DataTemplate static-resource resolution shape).

## Tests added/updated

Updated runtime tests:
- `tests/XamlToCSharpGenerator.Tests/Runtime/SourceGenDeferredServiceProviderFactoryTests.cs`
  - validates filtered resource stack plus upstream parent append behavior.
- `tests/XamlToCSharpGenerator.Tests/Runtime/SourceGenMarkupExtensionRuntimeTests.cs`
  - `ProvideDynamicResource_Returns_IBinding`
  - deferred-name resolution test using emitted writable `IPropertyInfo` descriptor path.

Updated generator tests:
- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
  - verifies CLR `TargetProperty` descriptor emission for x:Reference.
  - verifies dynamic-resource emits binding-style Avalonia-property assignment.

## Validation results
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter FullyQualifiedName~SourceGenDeferredServiceProviderFactoryTests|FullyQualifiedName~SourceGenMarkupExtensionRuntimeTests|FullyQualifiedName~AvaloniaXamlSourceGeneratorTests...`
  - Passed.
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter FullyQualifiedName~DifferentialFeatureCorpusTests`
  - Passed (`6/6` fixtures).
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`
  - Passed (`157` passed, `1` skipped).

## Remaining high-value parity backlog after this pass
- Add broader markup-extension parity for additional extension families beyond currently handled set.
- Add runtime differential execution fixtures (not only build diagnostics) for selected BasicTests lifecycle/deferred edge cases.
- Continue closure of non-service-context parity gaps tracked in Wave 7+ plans.
