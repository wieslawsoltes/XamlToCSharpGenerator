# WP2 Method + Null-Conditional Binding Implementation Report

## Scope delivered
This increment continues WP2 binding/query parity with two additions:
1. Compiled-binding method segment support (parameterless instance methods).
2. Compiled-binding null-conditional member access support (`?.`), including validation diagnostics.

## Code changes

### 1) Compiled-binding method segments
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. Path binding accessor resolver now falls back to parameterless instance methods when a property segment is not found.
2. Method calls emit accessor expressions like `source.ResolveTitle()`.
3. Optional explicit `()` token parsing is supported for method segments, with diagnostic rejection for non-parameterless call syntax.

### 2) Null-conditional (`?.`) compiled-binding segments
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. Segment parser now supports `?.` separators and tracks null-conditional behavior per segment.
2. Accessor emitter generates `?.` in compiled accessors (for example `source.SelectedPerson?.Name`).
3. Added semantic guard that rejects `?.` on non-nullable value types with AXSG0111 (path invalid) diagnostic path.
4. Normalized compiled binding path now preserves `?.` where present.

### 3) Supporting parser model updates
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. `CompiledPathSegment` now carries:
   - `CastTypeToken`
   - `IsMethodCall`
   - `AcceptsNull`
2. Segment parsing refactored to support casts, optional method-call tokens, indexers, and null-conditional separators together.

## Test coverage added

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added tests:
1. `Generates_Compiled_Binding_Accessor_With_Parameterless_Method_Segment`
2. `Generates_Compiled_Binding_Accessor_With_Null_Conditional_Segment`
3. `Reports_Diagnostic_For_Null_Conditional_On_NonNullable_Value_Type`

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 51, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 51, Failed: 0.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Remaining near-term WP2 items
1. Attached-property path segments in compiled binding paths (`(Owner.Property)` behavior parity).
2. Advanced grammar transforms from Avalonia `BindingExpressionGrammar`:
   - `!` transform node
   - `^` stream operator
   - richer method/command path semantics.
3. Deeper parity for name-scope/query transforms against XamlIl semantics across templates/scopes.
