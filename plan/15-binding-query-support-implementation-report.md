# Binding Query Support Implementation Report

## Scope
Implemented the next WP2-aligned increment after pass-engine work:
1. Basic binding query support for `#name` and `$parent` path syntax.
2. Compiled-binding gating for explicit-source bindings.

## Changes implemented

1. Query normalization in semantic binder
   - File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - Added normalization for binding paths:
     - `#SearchBox.Text` -> `ElementName = "SearchBox"`, `Path = "Text"`
     - `$parent.Tag` -> `RelativeSource = FindAncestor`, `Path = "Tag"`
     - `$parent[TypeName].Prop` and `$parent[TypeName,2].Prop` basic parsing support

2. Compiled-binding safety for non-data-context source bindings
   - Added `CanUseCompiledBinding(...)` guard.
   - Prevents compiled-binding path validation from incorrectly running when `ElementName` or `RelativeSource` is used as source.
   - Keeps runtime binding emission active for those cases.

## Tests added

1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
   - `Supports_ElementName_Query_Binding_Path_With_CompiledBindings_Enabled`
   - `Supports_Parent_Query_Binding_Path_With_CompiledBindings_Enabled`

## Validation

1. Command:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
2. Result:
   - Passed: 45
   - Failed: 0

## Known limits (still pending full parity)

1. This is a basic query subset, not full XamlIl query AST parity.
2. Advanced plugin/cast/method binding path semantics remain pending WP2 completion.
