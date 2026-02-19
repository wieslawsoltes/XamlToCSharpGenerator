# Wave 4 Tail + Wave 5 Tail Implementation Report (WS3.1 / WS3.2 / WS4)

Date: 2026-02-19

## Scope Executed
1. `WS3.1 tail`: selector grammar edge-case diagnostics hardening for strict invalid selector handling.
2. `WS3.2 tail`: control-template setter precedence parity for `SetValue` overload selection.
3. `WS4 tail`: control-template checker parity coverage expansion and deferred/template materialization reliability through read-only dictionary property-element paths.

## Implemented Changes

### 1) Read-only dictionary property-element materialization (`WS4` runtime differential gap)
1. Added dictionary-merge mode to property-element IR:
   - `ResolvedPropertyElementAssignment.IsDictionaryMerge`.
2. Binder now detects read-only dictionary-like properties (`Add(key, value)` capable) and emits merge-mode assignments instead of `AXSG0101` unsupported-property diagnostics.
3. Emitter now supports merge-mode property elements by:
   - fast path: emitting direct keyed child adds for dictionary-attachment nodes,
   - fallback path: helper merge (`__TryMergeDictionary`) for `IDictionary` instances.

Result:
1. `UserControl.Resources`-style read-only property elements now materialize child templates/resources instead of being dropped.
2. ControlTemplate content inside such dictionaries now reaches deferred-content emission and precedence paths.

### 2) Control-template precedence parity completion (`WS3.2`)
1. Existing priority-overload emission behavior was validated through now-working read-only resources path.
2. Confirmed behavior:
   - when 3-arg `SetValue(..., BindingPriority)` overload exists and template scope applies, emitted call uses `BindingPriority.Template`.
   - when overload is unavailable, emitted call falls back to 2-arg `SetValue`.

### 3) Selector strict-invalid diagnostics edge coverage (`WS3.1`)
1. Added strict regression fixtures for:
   - property predicate without type context,
   - malformed `nth-child(...)` syntax.
2. Verified both produce `AXSG0300` with concrete parser-like messages and mapped source locations.

### 4) ControlTemplate checker fixture expansion (`WS4`)
1. Added optional template-part missing diagnostic fixture (`AXSG0504`) to complement required-part (`AXSG0502`) and wrong-type (`AXSG0503`) fixtures.

## Test Additions
1. `Reports_Diagnostic_For_Invalid_Style_Selector_Property_Predicate_Without_Type_Context`
2. `Reports_Diagnostic_For_Invalid_Style_Selector_NthChild_Syntax`
3. `Reports_Diagnostic_For_Missing_Optional_ControlTemplate_Part`
4. Strengthened existing precedence fixtures by asserting no `AXSG0101` in read-only resources path.

## Validation
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `97`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

## Remaining Outside This Slice
1. Full `WS3.3` control-theme runtime materialization parity remains a broader track.
2. `WS5+` include graph and resource precedence parity across multi-file merge graphs remains pending.
