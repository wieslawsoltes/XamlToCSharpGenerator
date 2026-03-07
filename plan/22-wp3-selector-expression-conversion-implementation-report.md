# WP3 Selector Expression Conversion Implementation Report

## Scope delivered
Implemented the next WP3 selector/style parity increment in selector value conversion:
1. Expanded selector-to-C# conversion beyond simple `Type`, `.class`, `#name`.
2. Added support for selector combinators and structure used by Avalonia selectors:
   - descendant whitespace
   - child combinator (`>`)
   - template axis (`/template/`)
   - pseudo-classes (`:state`) mapped to selector class tokens.
3. Added selector OR-branch conversion support (`,` branches).
4. Fixed nullable selector type handling so `Selector?` properties are converted (previously fell back to AXSG0102).

## Code changes

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Implemented:
1. New selector conversion pipeline:
   - `TryBuildSimpleSelectorExpression(...)`
   - `TryBuildSimpleSelectorBranchExpression(...)`
   - `TryTokenizeSelectorBranch(...)`
   - `TryReadSelectorTypeToken(...)`
   - `IsSelectorTemplateAxisAt(...)`
2. Added selector conversion model helpers:
   - `SelectorCombinatorKind`
   - `SelectorBranchSegment`
   - `SelectorTemplateAxisToken`
3. Added selector type predicate:
   - `IsAvaloniaSelectorType(...)` supports both `Avalonia.Styling.Selector` and nullable `Avalonia.Styling.Selector?`.

## Test coverage added

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added tests:
1. `Converts_Selector_Value_With_Combinators_And_PseudoClasses`
2. `Converts_Selector_Value_With_Or_Branches`

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 60, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 60, Failed: 0.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Notes on parity status
1. Selector expression conversion now covers significantly more real style selector syntax used in Avalonia apps.
2. Advanced selector functions and predicates (for example `:is(...)`, `:not(...)`, property predicate selectors) are still pending broader WP3 parser parity work.
