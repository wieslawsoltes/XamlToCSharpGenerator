# XAML Expressions - Implementation Report

## Scope Completed
Implemented XAML expressions in SourceGen for Avalonia with end-to-end generation/runtime wiring, docs, tests, and sample coverage.

## Implemented

### 1) Binder expression pipeline
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Added:
- Explicit expression parsing: `{= ...}`.
- Implicit expression detection for safe C# payloads in `{ ... }`.
- Markup-extension precedence guards (known extension names + semantic resolver fallback).
- Expression normalization:
  - operator aliases (`AND/OR/LT/GT/LTE/GTE`)
  - single-quoted multi-char literal normalization.
- Source-member rewrite (`Member` -> `source.Member`) with lambda/local scope protection.
- Roslyn compile validation for rewritten expressions.
- Dependency extraction from rewritten source-member accesses.
- Avalonia-property assignment integration to emit expression runtime bindings.

Diagnostics behavior:
- Missing `x:DataType` for expression bindings -> `AXSG0110`.
- Invalid expression/rewrite/compile failure -> `AXSG0111`.

### 2) Runtime expression binding materialization
Files:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenMarkupExtensionRuntime.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenExpressionMultiValueConverter.cs`

Added:
- `SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<TSource>(...)`.
- Runtime materialization via `MultiBinding`:
  - first binding: `Binding(".")` to pass current source instance,
  - dependency bindings to trigger reevaluation,
  - converter invoking generated typed evaluator delegate.

### 3) Emitter binding-path detection
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

Added expression runtime-call recognition in `LooksLikeBindingExpression(...)` so expression bindings are emitted through Avalonia binding indexer assignment.

### 4) Tests
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added coverage:
- explicit expression binding generation.
- implicit expression binding generation.
- diagnostic for missing `x:DataType`.

### 5) Docs + sample catalog
Files:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/README.md`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/Pages/MarkupExtensionsPage.axaml`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/ViewModels/MarkupExtensionsPageViewModel.cs`

Added:
- README section for expression syntax (syntax, behavior, requirements).
- Catalog examples for explicit/implicit/operator-alias expressions.

## Validation
Executed:
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal`
  - Result: Passed 239, Failed 0, Skipped 1.
- `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj -v minimal`
  - Result: success, 0 warnings, 0 errors.
- `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -v minimal`
  - Result: success, 0 warnings, 0 errors.

## Notes
- Expression reevaluation currently follows dependency-driven `MultiBinding` updates and does not yet include deep nested-path dependency graph tracking.
