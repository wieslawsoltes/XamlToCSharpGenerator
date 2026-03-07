# WP2 Attached-Property Binding Path Implementation Report

## Scope delivered
This increment extends compiled-binding path parity with attached-property path segment support:
1. Parse attached-property path segments in binding paths: `(Owner.Property)`.
2. Resolve owner type from XAML namespace prefixes.
3. Emit accessor expressions via static getter pattern `Owner.GetProperty(...)`.

## Code changes

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. Compiled path segment model expanded with:
   - `IsAttachedProperty`
   - `AttachedOwnerTypeToken`
2. Segment parser now disambiguates:
   - cast segment: `(Type)Member` or `((Type)Member)`
   - attached-property segment: `(Owner.Property)`
3. Accessor emitter now handles attached-property segments by:
   - resolving owner type from type token and document namespaces,
   - resolving static getter method `Get{Property}` with compatible one-parameter signature,
   - emitting call expression, e.g. `global::Avalonia.Controls.Grid.GetRow(source.Item)`.
4. Added helper functions for getter resolution and assignability checks.

## Test coverage added

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added test:
1. `Generates_Compiled_Binding_Accessor_With_Attached_Property_Segment`

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 52, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 52, Failed: 0.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Remaining WP2 parity work
1. Transform-node parity: logical `!` and stream `^` behavior.
2. Broader method-path parity (beyond parameterless method getter shape).
3. Deep name-scope and ancestor semantics parity inside nested templates and deferred scopes.
