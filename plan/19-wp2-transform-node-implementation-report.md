# WP2 Transform-Node Implementation Report (`!` and `^`)

## Scope delivered
Implemented the next WP2 binding grammar increment focused on transform-node behavior:
1. Added compiled-binding path support for leading logical negation (`!`).
2. Added explicit stream-operator (`^`) recognition with deterministic invalid-path diagnostic messaging.

## Code changes

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. Compiled-binding path parser now tracks leading `!` count.
2. Accessor emission now applies negation wrappers:
   - `!global::System.Convert.ToBoolean(<expr>)`
   - repeated if path contains multiple leading `!`.
3. Parser now detects per-segment `^` stream operators and records count.
4. Accessor builder now emits clear invalid-path error for stream operator usage:
   - `"stream operator '^' is not supported yet in source-generated compiled binding paths"`
   - surfaced through existing AXSG0111 diagnostics pipeline.
5. Extended compiled-path segment model with `StreamCount`.

## Test coverage added

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added tests:
1. `Generates_Compiled_Binding_Accessor_With_Not_Transform`
2. `Reports_Diagnostic_For_Stream_Operator_In_Compiled_Binding_Path`

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 54, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 54, Failed: 0.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Notes on parity status
1. `!` is now modeled in compiled-binding accessor generation.
2. `^` is recognized and diagnosed deterministically; full runtime stream semantics are still pending deeper WP2/WP4 parity work.
