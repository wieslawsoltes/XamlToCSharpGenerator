# Feature-Completion Execution Tracker

This file tracks implemented slices from `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/23-feature-complete-remaining-master-plan.md`.

## Wave 1: Classless AXAML Generation Foundation

### Scope
1. Allow parser to keep classless documents (emit warning, continue model generation).
2. Support binder/generator/emitter flow for classless documents.
3. Emit static artifact class with module initializer and URI registration for classless documents.
4. Add tests for classless parse/generation.

### Status
1. `Completed`

### Tasks
1. [x] Update `XamlDocumentModel` to represent class-backed and classless documents.
2. [x] Update parser `AXSG0002` behavior from hard-stop to continue-with-warning.
3. [x] Update binder class-symbol resolution for optional class identity.
4. [x] Update emitter with classless artifact branch (no `InitializeComponent`, keep object-graph factory and registration).
5. [x] Update generator emission failure reporting for optional class identity.
6. [x] Add parser test for classless document parse behavior.
7. [x] Add generator test for classless registration/artifact emission.
8. [x] Run test/build matrix and record results.

### Exit Criteria
1. Classless AXAML produces generated source and module-initializer URI registration.
2. Existing class-backed generation behavior remains passing.
3. Full test suite and sample build are green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `65`, Failed: `0`.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `65`, Failed: `0`.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Wave 2: Binding Option Parity and Binder Stabilization

### Scope
1. Restore compiler health after incomplete binding refactor left the solution uncompilable.
2. Extend `BindingMarkup` model and parser to include additional named arguments.
3. Emit runtime binding initializer values for `Source`, `ConverterCulture`, `Priority`, and `UpdateSourceTrigger` in addition to existing options.
4. Preserve parsed binding options when query-syntax normalization rewrites path (`#name`, `$self`, `$parent`).
5. Add regression tests for advanced binding options and query-normalization option preservation.

### Status
1. `Completed`

### Tasks
1. [x] Fix binder runtime-binding call-site signature mismatch in `TryBindAvaloniaPropertyAssignment`.
2. [x] Extend `BindingMarkup` with option fields: `Source`, `Converter`, `ConverterCulture`, `ConverterParameter`, `StringFormat`, `FallbackValue`, `TargetNullValue`, `Delay`, `Priority`, `UpdateSourceTrigger`.
3. [x] Parse corresponding named arguments from markup extensions.
4. [x] Preserve option fields in `NormalizeBindingQuerySyntax(...)` rewrite branches.
5. [x] Emit additional runtime initializer assignments for binding options when target `Binding` type exposes writable properties.
6. [x] Add literal `ConverterCulture` conversion to strongly typed `CultureInfo` emission.
7. [x] Add generator regression tests:
   - `Generates_Runtime_Binding_Initializer_Options_For_Supported_Binding_Properties`
   - `Preserves_Binding_Options_When_Query_Path_Is_Normalized`
8. [x] Run full test/build validation matrix.

### Exit Criteria
1. Solution and tests compile and pass after binding option changes.
2. Generated runtime `Binding` initializer includes new supported options when available.
3. Query-based binding path normalization does not drop other binding options.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `67`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `67`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

### Still Open from WS2
1. Compiled-binding stream operator (`^`) semantics are still diagnostic-only (`AXSG0111`) and need dedicated parity implementation.

## Wave 3A: Stream Operator (`^`) Support for Compiled Binding Paths

### Scope
1. Implement stream operator lowering for compiled binding accessor generation.
2. Support stream unwrapping for `Task<T>`, `Task`, and `IObservable<T>` path segments.
3. Preserve normalized path output with stream markers.
4. Add runtime helper support for stream unwrapping.
5. Add regression tests for task/observable stream success and non-stream type diagnostics.

### Status
1. `Completed`

### Tasks
1. [x] Replace binder hard-fail on stream segments with stream-aware lowering.
2. [x] Add stream type-resolution helpers for `Task`/`IObservable` segment types.
3. [x] Add runtime helper `SourceGenCompiledBindingStreamHelper` with:
   - `UnwrapTask<T>(Task<T>?)`
   - `UnwrapTask(Task?)`
   - `UnwrapObservable<T>(IObservable<T>?)`
4. [x] Add generator tests:
   - `Generates_Compiled_Binding_Accessor_With_Task_Stream_Operator`
   - `Generates_Compiled_Binding_Accessor_With_Observable_Stream_Operator`
   - `Reports_Diagnostic_For_Stream_Operator_On_Non_Stream_Type`
5. [x] Add runtime helper unit tests in `SourceGenCompiledBindingStreamHelperTests`.
6. [x] Run full build/test/sample validation matrix.

### Exit Criteria
1. Stream operator no longer fails for supported streamable types.
2. Stream operator still reports deterministic diagnostics on unsupported types.
3. Runtime and generator test suites remain green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `72`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `72`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

### Still Open from WS2
1. Query plugin completeness (`$parent`/`$self` mixed with explicit source mode conflicts and advanced combinations).
2. Additional binding argument conversions and alias completeness (`WS2.B3`).

## Wave 3B: Query-Plugin Conflict Parity and Binding Source Alias Closure

### Scope
1. Add deterministic conflict handling when binding path query source syntax (`#name`, `$self`, `$parent`) is mixed with explicit `ElementName`, `RelativeSource`, or `Source`.
2. Normalize `Source={x:Reference ...}` to `ElementName` for generated runtime binding initializers.
3. Extend relative-source markup parsing aliases (`AncestorLevel`/`FindAncestor`/`Level`, `Tree`/`TreeType`).
4. Ensure compiled-binding registration is not attempted when explicit source overrides are present.
5. Add regression tests for conflict diagnostics, typed parent query syntax, and x:Reference source normalization.

### Status
1. `Completed`

### Tasks
1. [x] Extended `BindingMarkup` with source-conflict metadata (`HasSourceConflict`, `SourceConflictMessage`).
2. [x] Added conflict checks during query normalization and explicit source parsing.
3. [x] Added binder diagnostic reporting path for source conflicts (`AXSG0111`) across:
   - object property assignments,
   - Avalonia property assignments,
   - style/control-theme compiled-binding registration.
4. [x] Added `Source={x:Reference ...}` normalization to `ElementName`.
5. [x] Updated `CanUseCompiledBinding(...)` gating to exclude explicit `Source` and conflict states.
6. [x] Added relative-source alias parsing support for level/tree arguments.
7. [x] Added generator tests:
   - `Supports_Parent_Query_With_Type_And_Level_Using_Semicolon_Syntax`
   - `Converts_Source_XReference_To_ElementName_Binding_Source`
   - `Reports_Diagnostic_For_Query_Source_Conflict_With_ElementName`
   - `Reports_Diagnostic_For_Query_Source_Conflict_With_Source`
8. [x] Ran full build/test/sample validation matrix.

### Exit Criteria
1. Mixed-source query bindings emit deterministic diagnostics instead of silently combining source modes.
2. x:Reference source patterns used in bindings are emitted as equivalent `ElementName` runtime bindings.
3. Existing binding and compiled-binding suites remain green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `76`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `76`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

### Still Open from WS2
1. General-purpose `x:Reference` object materialization outside binding-source normalization (`Source`) is not yet implemented.

## Wave 3C: General `x:Reference` Materialization Support

### Scope
1. Add non-binding-source `x:Reference` conversion for generated property assignments.
2. Introduce runtime helper-based namescope resolution for generated x:Reference expressions.
3. Add runtime and generator regression tests for helper-based name resolution.

### Status
1. `Completed`

### Tasks
1. [x] Added `SourceGenNameReferenceHelper.ResolveByName(object?, string)` in runtime package.
2. [x] Extended markup conversion to emit helper calls for:
   - `{x:Reference Name}`,
   - `{Reference Name}`,
   - `{ResolveByName Name}`.
3. [x] Added generator regression test:
   - `Generates_Name_Reference_Helper_For_XReference_Value`.
4. [x] Added runtime regression tests:
   - `ResolveByName_Returns_Value_From_Direct_NameScope`
   - `ResolveByName_Returns_Value_From_StyledElement_NameScope`
5. [x] Ran full build/test/sample validation matrix.

### Exit Criteria
1. x:Reference expressions in non-binding-source property assignments emit valid generated code.
2. Runtime helper resolves direct and styled-element namescope references.
3. Build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `79`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `79`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

### Still Open from WS2
1. No open WS2 binding-semantic parity items remain in the current execution scope.

## Wave 4A: Selector Grammar Expansion (`WS3.1` Partial)

### Scope
1. Extend selector conversion beyond basic type/class/name/combinators.
2. Add pseudo-function support: `:is(...)`, `:not(...)`, `:nth-child(...)`, `:nth-last-child(...)`.
3. Add property predicate selector support: `[Property=Value]` with Avalonia property field resolution.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Extended selector segment parser to handle balanced `(...)` and `[...]` constructs.
2. [x] Added pseudo-function lowering:
   - `Selectors.Is(...)`
   - `Selectors.Not(...)`
   - `Selectors.NthChild(...)`
   - `Selectors.NthLastChild(...)`
3. [x] Added nth-child argument parser parity for integer, `an+b`, `odd`, and `even` patterns.
4. [x] Added property predicate lowering:
   - selector token parse (`[Property=Value]`)
   - Avalonia property field resolution
   - value literal conversion and `Selectors.PropertyEquals(...)` emission
5. [x] Added generator tests:
   - `Converts_Selector_Value_With_Pseudo_Functions`
   - `Converts_Selector_Value_With_Property_Equals_Predicate`
   - `Converts_Selector_Value_With_Attached_Property_Predicate`
6. [x] Ran full build/test/sample validation matrix.

### Exit Criteria
1. Supported pseudo-functions and property predicates emit deterministic selector C#.
2. Existing selector conversion behavior remains green.
3. Full build/test/sample validation remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `82`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `82`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

### Still Open from WS3
1. Additional selector grammar parity still open: escaped token handling, broader `:not(...)`/nested predicate edge cases, and remaining transformer-matrix selector forms.

## Wave 4B: Selector Edge Cases + Setter Precedence (`WS3.1` / `WS3.2` Slice)

### Scope
1. Close additional selector lowering edge cases for nested style selectors using inherited target context.
2. Improve selector token handling and nested `:not(...)` branch argument lowering coverage.
3. Add default binding-priority parity for runtime bindings emitted in style/control-theme/template scopes.
4. Add typed setter-value conversion in style/control-theme metadata binding pass.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Added selector fallback type propagation through `TryBuildSimpleSelectorExpression(...)` and recursive `:not(...)` lowering.
2. [x] Added selector token validation helpers aligned to style-token semantics (`IsSelectorTokenStart/Part`).
3. [x] Added binding-priority scope model (`None`/`Style`/`Template`) and scope propagation through binder recursion.
4. [x] Added default binding priority mapping:
   - style/control-theme contexts -> `BindingPriority.Style`
   - control-template contexts -> `BindingPriority.Template`
5. [x] Added typed setter value conversion in `BindStyles(...)` and `BindControlThemes(...)`.
6. [x] Added generator tests:
   - `Converts_Nested_Selector_Predicate_Using_Inherited_Target_Type`
   - `Converts_Selector_Not_Function_With_Or_Argument`
   - `Applies_Default_Style_BindingPriority_For_Style_Setter_Binding`
   - `Preserves_Explicit_BindingPriority_In_Style_Setter_Binding`

### Exit Criteria
1. Nested selector predicate lowering works with inherited target-type context.
2. Style/theme runtime binding emission includes deterministic default priority where unset.
3. Explicit binding priority remains authoritative.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `88`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `88`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

## Wave 5A: Deferred Template Materialization (`WS4.1` / `WS4.2` Slice)

### Scope
1. Materialize deferred-template content factories in generated C# instead of eager template-child instantiation.
2. Add content-property aware child attachment support (`[Content]` attributes), including non-`Content` content properties such as `Setter.Value`.
3. Expand template family coverage for deferred generation path.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Extended `ResolvedObjectNode` with `ContentPropertyName` metadata.
2. [x] Implemented binder content-property detection via `Avalonia.Metadata.ContentAttribute`.
3. [x] Reworked child-attachment resolution to carry content property names through emission.
4. [x] Added deferred template materialization path in emitter for:
   - `Template`
   - `DataTemplate`
   - `TreeDataTemplate`
   - `ControlTemplate`
   - `ItemsPanelTemplate`
   - `FocusAdornerTemplate`
5. [x] Added deferred template lambda emission:
   - `Func<IServiceProvider?, object?>` content factories
   - `TemplateResult<Control>` creation
   - template-local namescope creation and registration.
6. [x] Added generator tests:
   - `Materializes_ControlTemplate_Content_As_Deferred_Template_Factory`
   - `Attaches_Setter_Content_To_ContentAttributed_Value_Property`

### Exit Criteria
1. Template payloads are emitted as deferred factories rather than eagerly instantiated content trees.
2. Content-attributed nodes correctly attach child content to the declared content property.
3. New template/materialization tests pass with full solution validation.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `88`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `88`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false`
   - Build succeeded.

### Still Open from WS4
1. Full `ControlTemplate` validation parity (template-part/type diagnostics and control-template-specific checker parity) remains incomplete.
2. `TreeDataTemplate`/`ItemsPanelTemplate` runtime checker parity and dedicated fixture differentials remain pending.

## Wave 4C + Wave 5B: WS3.1/WS3.2 Tail + WS4 ControlTemplate Differential Slice

### Scope
1. Close strict invalid-selector diagnostics edge cases for style selectors.
2. Complete control-template setter precedence differential behavior for `SetValue` priority overload vs fallback.
3. Complete control-template checker fixture parity coverage, including optional template-part diagnostics.
4. Ensure deferred/template materialization still executes when templates are hosted under read-only dictionary property elements.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Added read-only dictionary property-element merge mode in semantic IR (`IsDictionaryMerge`).
2. [x] Added binder detection for dictionary-merge property elements (`CanMergeDictionaryProperty`).
3. [x] Added emitter dictionary-merge paths:
   - direct keyed child materialization for dictionary-attachment object values,
   - fallback `IDictionary` merge helper (`__TryMergeDictionary`).
4. [x] Verified control-template precedence parity behavior in generated output:
   - emit 3-arg `SetValue(..., BindingPriority.Template)` when supported,
   - emit 2-arg `SetValue(...)` when overload is unavailable.
5. [x] Added strict invalid-selector diagnostics fixtures:
   - property predicate without type context,
   - invalid `nth-child(...)` argument grammar.
6. [x] Added control-template optional part fixture (`AXSG0504`) in addition to required/wrong-type checks.

### Exit Criteria
1. Invalid selector edge cases produce deterministic `AXSG0300` with mapped source locations.
2. Control-template precedence overload behavior is deterministic across overload/no-overload targets.
3. Control-template part diagnostics cover required, optional, and wrong-type conditions.
4. Full test/build validation remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `97`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open from WS4+
1. `TreeDataTemplate`/`ItemsPanelTemplate` runtime checker differential parity remains pending.
2. Broader include/merge/resource precedence parity work (`WS5`) remains pending.

## Wave 5C: Template Runtime Checker Parity (`WS4`)

### Scope
1. Add runtime-checker parity for item-container misuse inside `ItemTemplate`/`DataTemplates`.
2. Add template content-root type compatibility checks for template families with runtime result-type expectations.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Added binder parity for item-container-in-template warning behavior.
2. [x] Added known container mapping parity for common `ItemsControl` families.
3. [x] Added template content root-type checker for:
   - `ItemsPanelTemplate` => `Panel`
   - `DataTemplate` => `Control`
   - `TreeDataTemplate` => `Control`
   - `ControlTemplate` => `Control`
4. [x] Added diagnostics:
   - `AXSG0505` (`ItemContainerInsideTemplate`)
   - `AXSG0506` (`TemplateContentTypeInvalid`)
5. [x] Added regression tests for container misuse and template-root type mismatch scenarios.

### Exit Criteria
1. Item-container misuse in data templates is detected with deterministic template diagnostics.
2. Invalid template content root types emit deterministic diagnostics with source mapping.
3. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `101`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. `WS5` include graph/merge precedence/resource lookup parity.
2. `WS3.3` control-theme runtime materialization parity.

## Wave 6A: Include Graph + Runtime URI Hardening + ControlTheme BasedOn Diagnostics (`WS5.1` / `WS6.3` / `WS3.3` Slice)

### Scope
1. Add global cross-file include graph analysis in the generator pipeline.
2. Add deterministic diagnostics for unresolved local include targets and include cycles.
3. Add duplicate generated URI target diagnostics to prevent runtime registration conflicts.
4. Add runtime include graph registry for direct/transitive include traversal.
5. Harden runtime URI registry behavior for duplicate registration and missing lookup observability.
6. Add ControlTheme `BasedOn` missing-target and cycle diagnostics.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Extended include semantic model with `ResolvedSourceUri` and `IsProjectLocal`.
2. [x] Added include URI resolution in binder with rooted/relative/avares normalization.
3. [x] Added include graph edge emission (`XamlIncludeGraphRegistry.Register(...)`) for project-local includes.
4. [x] Added generator global graph diagnostics:
   - `AXSG0403` include target not found in source-generated set.
   - `AXSG0404` include cycle detected.
   - `AXSG0601` duplicate generated URI target registration.
5. [x] Added runtime graph API `XamlIncludeGraphRegistry` with deterministic direct/transitive traversal.
6. [x] Hardened `XamlSourceGenRegistry` with:
   - duplicate URI registration event,
   - missing URI lookup event,
   - explicit `Clear()` for tests.
7. [x] Added ControlTheme diagnostics:
   - `AXSG0305` `BasedOn` key not found,
   - `AXSG0306` `BasedOn` cycle detected.
8. [x] Added generator/runtime tests for all above behaviors.

### Exit Criteria
1. Include graph diagnostics are deterministic and line-mapped.
2. Duplicate generated URI targets no longer rely on runtime silent overwrite behavior.
3. ControlTheme `BasedOn` missing/cycle conditions produce deterministic diagnostics.
4. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `110`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. `WS5.2` full merged-dictionary precedence and runtime resource lookup parity (beyond diagnostics/graph closure).
2. `WS3.3` control-theme runtime materialization parity (beyond `BasedOn` checker diagnostics).
3. `WS6.1` nested/template namescope parity completion and `WS6.2` stable source-info identity expansion.
4. `WS7` differential harness, determinism/perf gates, and release/migration closure.

## Wave 6B: Include-Aware Static Resource Resolution (`WS5.2` Slice)

### Scope
1. Extend static resource resolution to consult source-generated include graph (transitive `MergedDictionaries`).
2. Keep anchor/logical/application lookup semantics, but add source-generated include fallback path.
3. Route generated static-resource helper calls through runtime resolver with document URI context.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Added runtime resolver `SourceGenStaticResourceResolver.Resolve(object? anchor, object key, string currentUri)`.
2. [x] Added include-graph traversal fallback:
   - transitive includes from `XamlIncludeGraphRegistry`,
   - source-generated document materialization via `XamlSourceGenRegistry.TryCreate(...)`,
   - container resource lookup (`IResourceNode` and `IDictionary` paths).
3. [x] Updated emitted `__ResolveStaticResource(...)` helper to delegate to runtime resolver with build URI.
4. [x] Added runtime tests for transitive include resource resolution and missing-resource failure behavior.
5. [x] Added generator assertion that emitted resolver path includes URI-context delegation.

### Exit Criteria
1. StaticResource lookup can resolve values from transitive source-generated include graph for merged dictionaries.
2. Existing static-resource generation behavior remains passing.
3. Full test/build validation remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `112`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Full merged dictionary precedence parity beyond include-graph fallback semantics (theme-specific precedence and collision parity).
2. `WS3.3` control-theme runtime materialization parity (beyond `BasedOn` checker diagnostics).
3. `WS6.1` nested/template namescope parity completion and `WS6.2` stable source-info identity expansion.
4. `WS7` differential harness, determinism/perf gates, and release/migration closure.

## Wave 6C: NameScope + SourceInfo Parity Expansion (`WS6.1` / `WS6.2` Slice)

### Scope
1. Ensure template/nested named elements are registered into active NameScopes even without backing fields.
2. Add granular source-info emission for object/property/property-element and style/control-theme setter nodes.
3. Add deterministic runtime source-info retrieval/filter APIs for tooling and tests.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Extended `ResolvedObjectNode` with `Line` and `Column`.
2. [x] Propagated object source coordinates in binder materialization.
3. [x] Updated emitter name registration behavior:
   - decoupled NameScope registration from root-field assignment,
   - deferred-template `x:Name` registration now emits into template-local NameScope.
4. [x] Added recursive source-info emission for:
   - `Object`,
   - `Property`,
   - `PropertyElement`,
   - `StyleSetter`,
   - `ControlThemeSetter`.
5. [x] Added deterministic indexed source-info identities (`Kind:Index:Hint` and structural node paths).
6. [x] Extended `XamlSourceInfoRegistry` with:
   - deterministic ordering in `GetAll(...)`,
   - `GetByKind(...)`,
   - `TryGet(...)`,
   - `Clear()`.
7. [x] Added generator/runtime tests for template NameScope registration and source-info registry behavior.

### Exit Criteria
1. Deferred-template generated code emits NameScope registration for named template content.
2. Source-info output includes stable node identities for object/setter/property families.
3. Runtime source-info lookup APIs are deterministic and test-covered.
4. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `115`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Full merged dictionary precedence parity beyond include-graph fallback semantics (theme-specific precedence and collision parity).
2. `WS3.3` control-theme runtime materialization parity (beyond `BasedOn` checker diagnostics).
3. `WS7` differential harness, determinism/perf gates, and release/migration closure.

## Wave 6D: Include Ordering + Merged Dictionary Precedence (`WS5.2` Slice)

### Scope
1. Preserve include-edge registration order in runtime include graph for deterministic precedence evaluation.
2. Apply merged-dictionary duplicate-key precedence consistent with last include winning during static-resource fallback traversal.
3. Add runtime tests for include registration ordering and duplicate-key precedence behavior.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Extended `SourceGenIncludeEdgeDescriptor` with deterministic `Order` index.
2. [x] Added sequence-index assignment in `XamlIncludeGraphRegistry.Register(...)`.
3. [x] Updated include traversal ordering:
   - `GetDirect(...)` now returns registration order (`Order`) instead of URI-sorted order.
4. [x] Updated static-resource include fallback lookup:
   - `SourceGenStaticResourceResolver` now evaluates merged-dictionary transitive includes in reverse order to honor last-include precedence.
5. [x] Added runtime tests:
   - `XamlIncludeGraphRegistryTests.GetDirect_Preserves_Registration_Order`
   - `SourceGenStaticResourceResolverTests.Resolve_Uses_Last_Merged_Dictionary_Precedence_For_Duplicate_Keys`

### Exit Criteria
1. Include-edge order is deterministic and reflects declaration/registration sequence.
2. Duplicate keys across merged dictionaries resolve using last-include precedence in sourcegen include fallback path.
3. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `117`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Full merged dictionary precedence parity is still incomplete for theme-variant and broader collision scenarios.
2. `WS3.3` control-theme runtime materialization parity (beyond `BasedOn` checker diagnostics).
3. `WS7` differential harness, determinism/perf gates, and release/migration closure.

## Wave 6E: ControlTheme Runtime Materialization Completion (`WS3.3`)

### Scope
1. Move control-theme runtime path from metadata-only descriptors to generated-factory materialization.
2. Resolve `BasedOn` chains during runtime materialization for keyed themes.
3. Add target-type and theme-variant lookup path with default variant fallback.
4. Emit generated control-theme factory methods from source generator output.

### Status
1. `Completed`

### Tasks
1. [x] Extended `ResolvedSetterDefinition` with Avalonia-property owner/field metadata for style/control-theme setter materialization.
2. [x] Updated binder style/control-theme setter binding to capture static `<PropertyName>Property` field owner/name when resolvable.
3. [x] Expanded runtime `SourceGenControlThemeDescriptor` with:
   - `BasedOnKey`,
   - normalized theme variant,
   - optional generated factory delegate.
4. [x] Reworked `XamlControlThemeRegistry`:
   - deterministic registration storage,
   - overload accepting generated factory delegate,
   - `TryMaterialize(uri, key, out ControlTheme?)`,
   - `TryMaterialize(uri, targetType, themeVariant, out ControlTheme?)`,
   - `BasedOn` chain resolution,
   - default theme-variant fallback,
   - explicit `Clear()` for test isolation.
5. [x] Updated emitter control-theme registration to include generated factory delegate.
6. [x] Added per-theme generated method emission:
   - `__BuildGeneratedControlTheme{N}()`,
   - target type assignment,
   - resolved setter materialization into `ControlTheme.Setters`.
7. [x] Added runtime tests:
   - `TryMaterialize_By_Key_Resolves_BasedOn_Chain`,
   - `TryMaterialize_By_TargetType_Uses_ThemeVariant_And_Default_Fallback`,
   - `TryMaterialize_Returns_False_For_Metadata_Only_Registration`.
8. [x] Added generator test:
   - `Emits_ControlTheme_Materializer_Registration_With_Factory_Method`.

### Exit Criteria
1. Control-theme registration path supports generated runtime construction (not metadata-only).
2. `BasedOn` keyed chains materialize deterministically without recursion failures.
3. Theme-variant-target lookup supports exact match and default fallback behavior.
4. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `122`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Full merged dictionary precedence parity is still incomplete for theme-variant and broader collision scenarios.
2. `WS7` differential harness, determinism/perf gates, and release/migration closure.

## Wave 7A: Deterministic Output Baseline (`WS7.2` Slice)

### Scope
1. Add deterministic generated-source regression tests over repeated generator runs.
2. Add deterministic generated-source regression tests when AdditionalFiles ordering changes.
3. Provide reusable generator test harness access to `GeneratorDriverRunResult` for source-level comparisons.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Added generator test helper `RunGeneratorWithResult(...)` exposing `GeneratorDriverRunResult`.
2. [x] Added test `Generated_Sources_Are_Deterministic_Across_Repeated_Runs`.
3. [x] Added test `Generated_Sources_Are_Deterministic_When_AdditionalFile_Order_Changes`.
4. [x] Compared generated sources by stable hint name and source text content.

### Exit Criteria
1. Re-running generator with identical inputs produces byte-identical generated sources.
2. Changing AdditionalFiles input order does not alter generated source content.
3. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `124`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Full merged dictionary precedence parity is still incomplete for theme-variant and broader collision scenarios.
2. `WS7.1` dual-backend differential fixture harness remains pending.
3. `WS7.2` incremental perf benchmark harness remains pending.
4. `WS7.3` packaging/migration release closure remains pending.

## Wave 10A: Routed Event Transform Closure (`WS6.4` Slice)

### Scope
1. Close routed-event hookup parity beyond CLR events by supporting `FooEvent` static routed-event field resolution.
2. Emit routed-event rewiring with `RemoveHandler`/`AddHandler` instead of CLR event `-=`/`+=`.
3. Enforce handler delegate compatibility diagnostics for both CLR and routed event paths.

### Status
1. `Completed`

### Tasks
1. [x] Extended event subscription model to carry routed-event metadata:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedEventSubscription.cs`
2. [x] Implemented binder routed-event detection and compatibility validation:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
3. [x] Implemented emitter routing-specific event wiring:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
4. [x] Added generator tests for routed-event success and compatibility diagnostics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
5. [x] Updated matrix/evidence/report docs:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/04-parity-matrix.md`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/35-parity-evidence-dashboard.md`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/36-routed-event-transform-parity-closure-report.md`
6. [x] Extended source-info granularity with event-node registration identity:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~AvaloniaXamlSourceGeneratorTests"`
   - Passed.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`
   - Passed (`143` total, `0` failed, `1` skipped perf harness).

### Still Open
1. Full merged dictionary/include materialization parity remains partial in matrix-level differential coverage.
2. Template/deferred runtime differential corpus still needs expansion from build parity to runtime behavior parity.
3. Compiled binding grammar/plugin tail remains partial for complete XamlIl-level expression coverage.

## Wave 9: Final Parity Gap Closure (Watch/Differential/Perf/Sign-Off)

### Scope
1. Remove remaining `dotnet watch` duplicate AXAML source warnings.
2. Expand differential harness to feature-tagged parity corpus.
3. Promote perf harness to dedicated CI lane with enforced thresholds.
4. Publish parity evidence mapping and release-warning policy updates.

### Status
1. `Completed`

### Tasks
1. [x] Added final closure execution plan:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/34-final-parity-gap-closure-plan-and-execution.md`
2. [x] Fixed watch duplicate-source behavior by moving Avalonia compile disable to early props and hardening AXAML projection cleanup:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets`
3. [x] Added watch-mode regression coverage:
   - `SourceGen_Backend_WatchMode_Removes_AvaloniaXaml_And_Leaves_Deduplicated_AdditionalFiles`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`
4. [x] Added feature-tagged differential corpus:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialFeatureCorpusTests.cs`
   - feature tags: `bindings`, `styles`, `templates`, `resources`.
5. [x] Added dedicated perf-lane gating primitives:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerfFactAttribute.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerformanceHarnessTests.cs` (threshold env vars).
6. [x] Added CI workflow with standard + perf jobs:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/.github/workflows/ci.yml`
7. [x] Added parity evidence dashboard and matrix/checklist updates:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/35-parity-evidence-dashboard.md`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/04-parity-matrix.md`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/32-ws73-packaging-migration-and-release-checklist.md`

### Exit Criteria
1. Watch startup/session no longer emits duplicate AXAML source-file warnings.
2. Differential parity suite reports per-feature outcomes.
3. Perf tests are enforceable in dedicated CI lane.
4. Parity evidence references are auditable from docs.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --nologo -m:1 /nodeReuse:false --disable-build-servers --filter "FullyQualifiedName~BuildIntegrationTests"`
   - Passed: `4`, Failed: `0`.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --nologo -m:1 /nodeReuse:false --disable-build-servers --filter "FullyQualifiedName~Feature_Tagged_Fixture_Has_Equivalent_Backend_Build_Diagnostics"`
   - Passed: `4`, Failed: `0`.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `137`, Skipped: `1`, Failed: `0`.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
5. Manual watch verification:
   - `dotnet watch --verbose --project /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj`
   - duplicate AXAML source-file warnings are no longer emitted.

### Remaining
1. Non-parity release-hardening warning debt remains:
   - sample dependency vulnerability warning (`NU1903` on `SkiaSharp` transitive graph),
   - documentation/analyzer warning debt (`CS1591`, `RS2008`, `RS1036`).

## Wave 8: .NET 10 Migration (Repository-Wide)

### Scope
1. Move every project in the repository to `net10.0`.
2. Update package/runtime asset paths and test fixtures hardcoded to older TFMs.
3. Validate restore/build/test under `.NET 10` SDK.

### Status
1. `Completed`

### Tasks
1. [x] Migrated all `.csproj` target frameworks to `net10.0`:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlToCSharpGenerator.Runtime.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/XamlToCSharpGenerator.Build.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/XamlToCSharpGenerator.Core.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Generator/XamlToCSharpGenerator.Generator.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj`
2. [x] Updated top-level package runtime/analyzer asset paths to `net10.0`:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj`
3. [x] Updated build/package/integration tests that hardcoded `net8.0/net6.0`:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialBackendTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerformanceHarnessTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PackageIntegrationTests.cs`
4. [x] Ran restore/build/test validation on `.NET 10`.

### Exit Criteria
1. No project remains on legacy target frameworks.
2. Full solution build and test pass on `net10.0`.
3. Packaging assertions match `lib/net10.0` runtime assets.

### Validation Results
1. `dotnet restore /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx --nologo`
   - Succeeded.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Succeeded.
3. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --nologo -m:1 /nodeReuse:false --disable-build-servers --no-build`
   - Passed: `132`, Skipped: `1`, Failed: `0`.

### Notes
1. Existing warning set remains (for example CS1591 and NU1903 from sample transitive dependency), but no new migration-breaking errors were introduced.

## Wave 7C: Theme-Variant Resource Parity + Hint-Collision Hardening (`WS5.2` / `WS7` Slice)

### Scope
1. Close theme-variant merged-dictionary resource precedence parity coverage.
2. Harden generator against duplicate `AddSource` hint-name failures seen in `dotnet watch` sessions.
3. Stabilize SourceGen build target projection of AXAML into analyzer `AdditionalFiles` with deterministic metadata.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Hardened generator `AdditionalFiles` deduplication:
   - dedupe by normalized file path (instead of path+target tuple),
   - deterministic preferred target-path selection for duplicated path representations.
2. [x] Added safe source registration branch in generator:
   - `TryAddSource(...)` wrapper around `SourceProductionContext.AddSource(...)`,
   - duplicate hint-name `ArgumentException` now handled without generator failure.
3. [x] Updated hot-reload fallback source path to reuse safe source registration.
4. [x] Added runtime parity coverage for theme-variant resource lookup:
   - `Resolve_Passes_Anchor_ThemeVariant_To_Include_Graph_Resource_Nodes`,
   - `Resolve_Uses_Last_Merged_Dictionary_Precedence_For_ThemeVariant_Fallback_Collisions`.
5. [x] Added generator regression for duplicate path representation dedupe:
   - `Duplicate_Path_Representations_Are_Deduplicated_To_Avoid_HintName_Collisions`.
6. [x] Extended build integration coverage for duplicate-avoidance in projected `AdditionalFiles`:
   - `SourceGen_Backend_Rewrites_AvaloniaXaml_AdditionalFiles_Without_Duplicates`.
7. [x] Updated SourceGen build target projection behavior:
   - normalize and rewrite AXAML as `AdditionalFiles` with stable metadata,
   - remove `AvaloniaXaml` items from SourceGen backend compile surface after projection.
8. [x] Cleaned sample AXAML warning noise:
   - removed unsupported literal color assignment in sample list template to keep validation output clean.

### Exit Criteria
1. Duplicate hint-name generator crashes are eliminated in duplicate-path and watch-edit scenarios.
2. Theme-variant include-graph static resource lookup precedence is test-covered.
3. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `130`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.
4. Manual watch validation:
   - `dotnet watch --project ./SourceGenCrudSample.csproj` no longer reproduces prior `CS8785` duplicate hint-name generator failure during edit/rebuild cycle.

### Still Open
1. `dotnet watch` still reports duplicate AXAML source-file warnings at startup; behavior is non-fatal but should be fully eliminated in a follow-up watch-specific integration slice.
2. `WS7.1` dual-backend differential fixture harness remains pending.
3. `WS7.2` incremental perf benchmark harness remains pending.
4. `WS7.3` packaging/migration release closure remains pending.

## Wave 7D: Dual-Backend Differential Harness Baseline (`WS7.1` Slice)

### Scope
1. Add automated fixture harness that builds the same project under `SourceGen` and `XamlIl` backends.
2. Validate both backend builds succeed and produce compiled artifacts.
3. Assert SourceGen path emits generated `.XamlSourceGen.g.cs` output in fixture build.

### Status
1. `Completed (Baseline Slice)`

### Tasks
1. [x] Added dual-backend build harness test:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialBackendTests.cs`
   - `Simple_Fixture_Builds_With_Both_XamlIl_And_SourceGen_Backends`.
2. [x] Implemented temporary fixture project generation inside test:
   - minimal AXAML + C# partial types,
   - conditional SourceGen analyzer/runtime wiring,
   - conditional SourceGen build-transitive props/targets import.
3. [x] Added backend build orchestration:
   - restore,
   - SourceGen build + generated source file assertion,
   - clean,
   - XamlIl build assertion.
4. [x] Added regression assertions for generator stability:
   - no `CS8785` duplicate hint-name failures in either backend build output.

### Exit Criteria
1. Single fixture can be compiled under both backends in automated test suite.
2. SourceGen backend confirms generated source emission on fixture compile.
3. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `131`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Differential harness currently covers baseline build parity only; feature-tagged runtime-behavior differential corpus is still pending expansion.
2. `dotnet watch` still reports duplicate AXAML source-file warnings at startup; behavior is non-fatal but should be fully eliminated in a follow-up watch-specific integration slice.
3. `WS7.2` incremental perf benchmark harness remains pending.
4. `WS7.3` packaging/migration release closure remains pending.

## Wave 7E: Incremental Performance Harness Baseline (`WS7.2` Slice)

### Scope
1. Add repeatable harness code for full SourceGen build timing vs incremental AXAML edit rebuild timing.
2. Cover both single-file view edit and include/resource edit paths in the same harness fixture.
3. Keep harness isolated from main correctness suite flakiness by marking it opt-in.

### Status
1. `Completed (Baseline Slice)`

### Tasks
1. [x] Added performance harness fixture test scaffolding:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerformanceHarnessTests.cs`
2. [x] Implemented timing pipeline:
   - restore + clean,
   - full SourceGen build timing,
   - incremental rebuild timing after `MainWindow.axaml` edit,
   - incremental rebuild timing after included `Colors.axaml` edit.
3. [x] Added guardrail assertions for successful command execution and non-pathological incremental timing ratio.
4. [x] Marked harness as opt-in (`[Fact(Skip=...)]`) to avoid instability in default fast CI lane while preserving executable benchmark workflow.
5. [x] Hardened build process readers in build tests to avoid stdout/stderr deadlock risks:
   - `BuildIntegrationTests`,
   - `DifferentialBackendTests`,
   - `PerformanceHarnessTests`.

### Exit Criteria
1. Incremental perf harness code exists and can be enabled in dedicated perf lane.
2. Full build/test matrix remains green in default lane.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `131`, Skipped: `1`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Performance thresholds are not yet enforced in a dedicated CI perf lane (harness is currently opt-in).
2. Differential harness needs expansion to feature-tagged runtime behavior comparisons (beyond build parity baseline).
3. `dotnet watch` still reports duplicate AXAML source-file warnings at startup; behavior is non-fatal but should be fully eliminated in a follow-up watch-specific integration slice.
4. `WS7.3` packaging/migration release closure remains pending.

## Wave 7F: Packaging + Migration Closure (`WS7.3` Slice)

### Scope
1. Validate bundled top-level package asset layout end-to-end.
2. Publish concrete migration and compatibility guidance for backend switch adoption.
3. Publish release checklist for package and integration validation.

### Status
1. `Completed (Baseline Slice)`

### Tasks
1. [x] Added package integration validation test:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PackageIntegrationTests.cs`
   - packs `XamlToCSharpGenerator` and asserts required assets inside `.nupkg`.
2. [x] Expanded root README migration guidance:
   - backend switch flow,
   - optional SourceGen MSBuild knobs,
   - compatibility matrix,
   - diagnostic band mapping,
   - release checklist link.
3. [x] Added detailed packaging/migration/release checklist doc:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/32-ws73-packaging-migration-and-release-checklist.md`
4. [x] Hardened build-test process output handling in long-running process helpers to reduce deadlock risk in pack/build integration tests.

### Exit Criteria
1. Top-level package can be packed and validated for expected analyzer/runtime/build-transitive assets.
2. Migration and compatibility documentation is available in-repo for SourceGen adoption.
3. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers --filter "FullyQualifiedName~PackageIntegrationTests"`
   - Passed: `1`, Failed: `0`.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `132`, Skipped: `1`, Failed: `0`.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Differential harness needs expansion to feature-tagged runtime behavior comparisons (beyond baseline dual-build success).
2. Performance threshold enforcement remains opt-in; dedicated perf CI lane still pending.
3. `dotnet watch` still reports duplicate AXAML source-file warnings at startup; behavior is non-fatal but should be fully eliminated in a follow-up watch-specific integration slice.

## Wave 7B: Hot Reload Error-Resilience (`WS6/WS7 Hardening Slice)

### Scope
1. Prevent transient malformed AXAML edits from breaking watch/hot-reload cycles.
2. Reuse last-known-good generated source while current AXAML has parse/semantic/emit failures in hot-reload sessions.
3. Keep strict default behavior outside hot-reload sessions and when resilience is explicitly disabled.

### Status
1. `Completed (Slice)`

### Tasks
1. [x] Extended generator options and build contract with:
   - `AvaloniaSourceGenHotReloadErrorResilienceEnabled` (default `true`),
   - `DotNetWatchBuild` visibility to analyzer options.
2. [x] Added hot-reload fallback diagnostic:
   - `AXSG0700` (`HotReloadFallbackUsed`).
3. [x] Implemented generator-side last-known-good cache keyed by assembly + file + target path.
4. [x] Added hot-reload detection for resilience mode:
   - `build_property.DotNetWatchBuild`,
   - `DOTNET_WATCH=1`.
5. [x] Added fallback code path for parse/bind/emit failure:
   - reuse cached generated source,
   - report `AXSG0700`,
   - demote transient error diagnostics to warnings in resilience mode.
6. [x] Added generator tests:
   - `HotReload_WatchMode_Uses_Last_Good_Source_When_Xaml_Is_Temporarily_Invalid`,
   - `HotReload_Resilience_Can_Be_Disabled_To_Keep_Strict_Error_Behavior`.
7. [x] Added analysis and implementation docs:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/30-hot-reload-error-resilience-analysis-and-plan.md`,
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/31-hot-reload-error-resilience-implementation-report.md`.

### Exit Criteria
1. Invalid intermediate AXAML edits during watch mode do not force hard generator failure when fallback exists.
2. Last valid generated output remains active until user fixes AXAML.
3. Disabling resilience restores strict error behavior.
4. Full build/test matrix remains green.

### Validation Results
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `126`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

### Still Open
1. Full merged dictionary precedence parity is still incomplete for theme-variant and broader collision scenarios.
2. `WS7.1` dual-backend differential fixture harness remains pending.
3. `WS7.2` incremental perf benchmark harness remains pending.
4. `WS7.3` packaging/migration release closure remains pending.
