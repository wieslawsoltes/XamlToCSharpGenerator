# WP2 Method-Argument Path Implementation Report

## Scope delivered
Implemented the next WP2 compiled-binding increment for method-call segments with literal arguments:
1. Parsed method argument lists in compiled-binding path segments.
2. Bound method-call segments against instance overloads using typed argument conversion.
3. Emitted deterministic AXSG0111 diagnostics when no overload matches.

## Code changes

Files:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. Extended `CompiledPathSegment` to carry `MethodArguments`.
2. Wired method-argument data through compiled path parsing and accessor emission.
3. Added overload resolution for method-call segments:
   - instance, non-generic, non-void methods
   - parameter count must match path argument count
   - deterministic candidate selection order
4. Added literal token conversion support for method arguments:
   - `string`, `char`, `bool`, `int`, `long`, `float`, `double`, `object`, enum, and nullable value-type handling.
5. Restored missing helper utilities required by this path:
   - `IsNullableValueType(...)`
   - `EscapeChar(...)`

## Test coverage added

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added tests:
1. `Generates_Compiled_Binding_Accessor_With_Method_Arguments`
2. `Reports_Diagnostic_For_Compiled_Binding_Method_Argument_Mismatch`

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 56, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 56, Failed: 0.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Notes on parity status
1. Compiled-binding method-path calls now support literal arguments for core scalar and enum cases.
2. Expression-level arguments (nested member access, markup extensions, lambdas) remain out of scope and are still pending later parity milestones.
