# WP2 Query/Cast + Emitter Fixes Implementation Report

## Scope delivered
This increment advances WP2 binding/query parity and fixes a concrete emitter regression observed in generated UI code.

Implemented:
1. Compiled-binding cast path segment support for `((Type)Member)` syntax.
2. Query transform expansion for `$self` and corrected `$parent[2]` level-only syntax.
3. Emitter fix: do not overwrite explicit `Content=` values with `default!` when a control has no child nodes.

## Code changes

### 1) Compiled-binding cast path support
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Details:
1. Extended compiled path segment model with optional `CastTypeToken`.
2. Added segment parser support for casted member forms:
   - `((vm:Type)Member)`
   - `(vm:Type)Member` (accepted as relaxed form)
3. Updated accessor emitter pipeline to:
   - resolve cast type via existing type-token resolution,
   - emit casted accessor expressions,
   - produce a binding diagnostic when cast type token cannot be resolved.

### 2) Query parity increment (`$self`, `$parent[level]`)
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

Details:
1. Added `$self` query normalization:
   - `$self` -> `RelativeSource(Self), Path="."`
   - `$self.Prop` -> `RelativeSource(Self), Path="Prop"`
2. Corrected `$parent[...]` parser for single numeric argument:
   - `$parent[2].Tag` now maps to `RelativeSource(FindAncestor) { AncestorLevel = 2 }`
   - previous behavior incorrectly treated `2` as an ancestor type token.

### 3) Emitter content overwrite fix
File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

Details:
1. In `Content` attachment mode with no child objects, generated code now sets `Content = default!` only when there is no explicit `Content` assignment from attributes/property-elements.
2. Added robust content-property detection that handles:
   - case-insensitive `Content`
   - owner-qualified names like `Owner.Content`.

## Test coverage added/updated

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added/updated tests:
1. Updated existing object graph test to assert explicit content is not overwritten by `default!`.
2. Added `Generates_Compiled_Binding_Accessor_With_Casted_Path_Segment`.
3. Added `Supports_Self_Query_Binding_Path_With_CompiledBindings_Enabled`.
4. Added `Supports_Parent_Query_With_Level_Only_Syntax`.

## Validation

1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 48, Failed: 0.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: 48, Failed: 0.
3. Rebuilt sample with generated file output and verified `MainWindow` no longer emits `Button.Content = default!` after explicit content assignment.

## Remaining WP2/WP3 follow-up
1. Full BindingExpressionGrammar parity is still pending (methods, attached-property path nodes, null-conditional, stream/not operators).
2. Query/type resolution parity for namescopes and ancestor inference is still partial versus XamlIl.
3. Setter/style/control-theme value transform parity remains ongoing for advanced selector and setter-value semantics.
