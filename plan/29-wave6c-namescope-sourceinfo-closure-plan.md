# Wave 6C Plan: NameScope + SourceInfo Parity Closure Slice

Date: 2026-02-19

## Objective
Close the next high-value parity gap from `WS6.1` and `WS6.2` by implementing:
1. Template/nested `x:Name` registration into generated NameScopes (including deferred template scopes).
2. Stable, granular source-info registrations for object graph nodes and setter/property nodes.
3. Runtime source-info registry query/clear APIs to support deterministic tooling and tests.

## Scope
1. `AvaloniaCodeEmitter`:
   - Decouple root field assignment from NameScope registration so any named node can register in scope.
   - Ensure deferred template content registers named nodes into template-local NameScope.
   - Emit additional source-info entries for object/property/property-element/style-setter/control-theme-setter nodes.
   - Use deterministic identity keys based on structural traversal order.
2. `Core` and binder:
   - Extend `ResolvedObjectNode` with `Line` and `Column` to support node-level source-info emission.
3. `Runtime`:
   - Enhance `XamlSourceInfoRegistry` with deterministic ordering and query/clear APIs.
4. Tests:
   - Generator tests for template NameScope registration and granular source-info emission.
   - Runtime tests for source-info registry ordering/query behavior.

## Acceptance Criteria
1. Generated code contains template-scope `Register(...)` calls for `x:Name` inside deferred templates.
2. Generated source contains source-info registrations for object/property/setter nodes with stable identities.
3. Runtime source-info registry supports deterministic retrieval and filtered queries.
4. Full `XamlToCSharpGenerator.Tests` project remains green after changes.

## Execution Checklist
1. Update model + binder for object source coordinates.
2. Update emitter NameScope registration and source-info traversal.
3. Update runtime source-info registry API.
4. Add/adjust generator and runtime tests.
5. Run tests and record results.

## Status
1. `Completed (Slice)`

## Execution Log
1. Extended `ResolvedObjectNode` with `Line` and `Column` and propagated parser coordinates in binder emission.
2. Updated generated NameScope wiring:
   - NameScope registration now runs independently from generated backing-field assignment.
   - Deferred template scopes now register `x:Name` entries even when no root field exists.
3. Expanded source-info emission in `AvaloniaCodeEmitter`:
   - Added deterministic indexed identities for named/resources/templates/styles/control-themes/includes/compiled-bindings.
   - Added recursive object graph source-info for:
     - `Object`,
     - `Property`,
     - `PropertyElement`,
     - `StyleSetter`,
     - `ControlThemeSetter`.
4. Extended runtime source-info registry:
   - deterministic ordering in `GetAll(...)`,
   - filtered query `GetByKind(...)`,
   - targeted lookup `TryGet(...)`,
   - `Clear()` API for tests/hot-reload lifecycle tooling.
5. Added tests:
   - Generator: template `x:Name` registration assertion in deferred control-template factory output.
   - Generator: source-info assertions for object/named/style-setter registration shapes.
   - Runtime: new `XamlSourceInfoRegistryTests` for ordering/filter/query behavior.

## Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `115`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

## Remaining After This Slice
1. `WS5.2` merged dictionary precedence and theme-specific collision behavior parity still requires differential closure.
2. `WS3.3` control-theme runtime materialization parity is still metadata-first and needs full generated-materialization equivalence.
3. `WS7` differential harness/perf gates/packaging closure remains open.
