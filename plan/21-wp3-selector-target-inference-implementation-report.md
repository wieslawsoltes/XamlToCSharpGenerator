# WP3 Selector Target-Inference Implementation Report

## Scope delivered
Implemented the next WP3 style/selector parity increment focused on selector target-type inference:
1. Resolve style setter target type from the right-most selector segment (not only first token).
2. Support Avalonia selector namespace alias syntax (`prefix|Type`) for target-type resolution.
3. Preserve deterministic diagnostics behavior by avoiding ambiguous type inference on selector OR groups.

## Code changes

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. Replaced `TryExtractSelectorTypeToken` with branch/segment-aware parsing:
   - split selector branches by top-level `,`
   - require single branch for deterministic inference
   - split selector branch into segments across combinators (`>`, descendant whitespace, `/template/`)
   - infer target type from the right-most segment that has an explicit leading type token.
2. Added selector helper routines:
   - `SplitSelectorSegments(...)`
   - `TryExtractLeadingSelectorTypeToken(...)`
   - `AddSelectorSegment(...)`
3. Added namespace-alias conversion for selector type tokens:
   - `local|FancyControl` -> `local:FancyControl`
   - enables reuse of existing XAML namespace type resolver.

## Test coverage added

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added tests:
1. `Resolves_Style_Setter_Target_From_Rightmost_Selector_Type`
2. `Resolves_Style_Selector_Target_With_Namespace_Alias_Syntax`

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 58, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 58, Failed: 0.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Notes on parity status
1. Style setter target-type inference now aligns better with selector combinator semantics used in Avalonia styles.
2. Full selector AST/runtime materialization parity (pseudo-classes/functions/property predicates/not/or nesting) is still pending later WP3 milestones.
