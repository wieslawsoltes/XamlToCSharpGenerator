# AvaloniaSemanticBinder Reusable Extraction Wave 1

Date: 2026-02-23  
Status: In Progress (Wave 1 + Wave 2 + Wave 3 + Wave 4 + Wave 5 + Wave 6 + Wave 7 + Wave 8 + Wave 9 + Wave 10 + Wave 11 + Wave 12 + Wave 13 + Wave 14 + Wave 15 + Wave 16 + Wave 17 + Wave 18 core implemented in this change set)  
Scope: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

## 1. Goal

Extract framework-agnostic semantic logic from `AvaloniaSemanticBinder` into reusable projects, starting with low-risk/high-reuse clusters.

## 2. Hotspot Analysis

`AvaloniaSemanticBinder.cs` is ~15k lines and mixes:
1. Avalonia-specific property/resource/template semantics.
2. Framework-agnostic semantic algorithms (C# expression rewriting, dependency extraction, symbol validation).
3. Parser/tokenization helpers.

Reusable-candidate clusters identified:
1. C# source-context expression rewrite + dependency extraction (Roslyn-only, framework-agnostic).
2. Expression text normalization (alias replacement, single-quote normalization) (framework-agnostic).
3. Type/namespace deterministic resolver core shell (framework-neutral contract, framework profiles).
4. Binding/event markup token model (framework-neutral parse model, framework-specific projection).

## 3. Wave Plan

## Wave 1 (this implementation): C# expression semantic extraction

Deliverables:
1. New reusable project:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics`
2. Reusable API for:
   - parsing + rewriting source expressions to `source.<member>` access form,
   - dependency name extraction,
   - expression validation against Roslyn compilation/type.
3. `AvaloniaSemanticBinder` updated to consume the reusable API and remove equivalent in-file implementation.

Exit criteria:
1. No behavior drift in expression diagnostics (`AXSG0111`) and generated expressions.
2. Existing expression-related generator tests pass.

## Wave 2 (implemented): Expression text normalization extraction

Deliverables:
1. Move alias/token/literal normalization helpers to expression semantics project.
2. Keep markup-extension gating and framework heuristics in binder adapter.

Execution notes:
1. Added `CSharpExpressionTextSemantics` in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/CSharpExpressionTextSemantics.cs`.
2. Binder now consumes `CSharpExpressionTextSemantics` for normalization/operator-shape checks while keeping markup-extension gate in Avalonia binder.
3. Added dedicated tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CSharpExpressionTextSemanticsTests.cs`

## Wave 3 (implemented): Type-resolution reusable core shell

Deliverables:
1. Extract deterministic candidate ranking and ambiguity diagnostics scaffolding.
2. Keep Avalonia namespace seeds and metadata aliases as adapter profile.

Execution notes:
1. Added reusable deterministic resolver primitives in:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/DeterministicTypeResolutionSemantics.cs`
2. `AvaloniaSemanticBinder` now delegates namespace-prefix candidate collection, deterministic candidate selection, and generic-arity metadata-name normalization to the reusable semantics API.
3. Binder retains Avalonia-specific namespace seed/profile logic and `AXSG0112`/`AXSG0113` reporting policy.
4. Added dedicated tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/DeterministicTypeResolutionSemanticsTests.cs`

## Wave 4 (implemented): Binding/event markup token model extraction

Deliverables:
1. Move binding/event markup token models out of Avalonia binder into framework-neutral Core models.
2. Move binding/event markup parsing and normalization into reusable Core parser utilities.
3. Keep Avalonia binder as adapter/wrapper over shared parser APIs.

Execution notes:
1. Added framework-neutral model types:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/BindingEventMarkupModels.cs`
2. Added reusable parser + normalization API:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
3. Updated `AvaloniaSemanticBinder` to delegate binding/event markup parsing, source-query normalization, resolve-by-name token parsing, and event-binding token validation to `BindingEventMarkupParser`.
4. Added focused parser tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingEventMarkupParserTests.cs`

## Wave 5 (implemented): Event-binding path semantics extraction

Deliverables:
1. Move event-binding path/identifier parsing and method-name generation helpers into reusable Core parser semantics.
2. Keep binder event-binding resolution flow unchanged, but delegate path-shape helpers.

Execution notes:
1. Added reusable event-binding path semantics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/EventBindingPathSemantics.cs`
2. Updated `AvaloniaSemanticBinder` to delegate:
   - method-path split,
   - argument-set matrix selection,
   - simple path/identifier checks,
   - generated event-binding method-name building.
3. Added focused tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/EventBindingPathSemanticsTests.cs`

## Wave 6 (implemented): Type-token parsing semantics extraction

Deliverables:
1. Move generic type-token parsing and `clr-namespace` / `using` metadata-name construction into reusable type-resolution semantics.
2. Keep binder type-resolution orchestration unchanged, but delegate token-shape helpers.

Execution notes:
1. Extended reusable type-resolution semantics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/DeterministicTypeResolutionSemantics.cs`
   - Added:
     - `TryParseGenericTypeToken`
     - `TryBuildClrNamespaceMetadataName`
2. Updated binder delegation points:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
3. Added/extended tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/DeterministicTypeResolutionSemanticsTests.cs`

## Wave 7 (implemented): Selector predicate/pseudo token semantics extraction

Deliverables:
1. Add reusable selector token utilities and property-predicate parser helpers in the shared mini-language parser project.
2. Replace binder-local selector pseudo-function classification and property-predicate parsing with shared semantics.
3. Replace Avalonia selector syntax validator token helpers with shared selector syntax utilities while preserving existing diagnostics.

Execution notes:
1. Added reusable selector token/predicate semantics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorTokenSyntax.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorPropertyPredicateSyntax.cs`
   - Extended:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorPseudoSyntax.cs`
2. Updated binder delegation:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
3. Updated selector validation adapter to shared parser utilities:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/SelectorSyntaxValidator.cs`
4. Added/extended tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/MiniLanguageParsingTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

## Wave 8 (implemented): Selector validator extraction to shared parser project

Deliverables:
1. Move selector grammar validation core (`Validate`, branch analysis, pseudo/property predicate validation) from Avalonia binder layer into shared mini-language parser project.
2. Switch binder selector target-resolution contracts from Avalonia-local validator branch types to shared branch models.
3. Remove Avalonia-local selector validator implementation to eliminate duplicated parser semantics.

Execution notes:
1. Added shared selector validator contracts/engine:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorSyntaxValidator.cs`
   - Includes:
     - `SelectorBranchInfo`
     - `SelectorValidationResult`
     - `SelectorSyntaxValidator.Validate`
2. Updated binder target-type resolution contract:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - `TryResolveSelectorTargetType` now accepts `ImmutableArray<SelectorBranchInfo>`.
3. Removed Avalonia-local duplicate validator:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/SelectorSyntaxValidator.cs`

## Wave 9 (implemented): Selector target-type resolution semantics extraction

Deliverables:
1. Move selector branch target-type convergence algorithm from binder into reusable expression semantics.
2. Keep binder-specific symbol resolution as adapter delegate only.
3. Preserve existing unresolved-type diagnostics behavior (token + offset).

Execution notes:
1. Added reusable selector target-type resolution semantics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/SelectorTargetTypeResolutionSemantics.cs`
2. Updated binder delegation:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - `TryResolveSelectorTargetType` now delegates to `SelectorTargetTypeResolutionSemantics.ResolveTargetType`.
3. Added dedicated tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/SelectorTargetTypeResolutionSemanticsTests.cs`
4. Extended de-hack guard:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

## Wave 10 (implemented): Selector expression assembly extraction with emitter contract

Deliverables:
1. Move selector expression assembly control-flow out of binder into reusable semantics with an emitter abstraction.
2. Keep framework-specific selector API call-shape in an Avalonia emitter adapter.
3. Keep property-predicate conversion in binder via callback contract to preserve typed Avalonia property resolution behavior.

Execution notes:
1. Added reusable selector expression assembly semantics:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/SelectorExpressionBuildSemantics.cs`
   - Includes:
     - `ISelectorExpressionEmitter`
     - `SelectorPropertyPredicateApplier`
     - `SelectorExpressionBuildSemantics.TryBuildSelectorExpression`
2. Added Avalonia emitter adapter:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorExpressionEmitter.cs`
3. Updated binder delegation:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - `TryBuildSimpleSelectorExpression` now delegates to `SelectorExpressionBuildSemantics`.
   - Removed binder-local selector branch/pseudo/combinator assembly helpers that duplicated parser semantics.
4. Added dedicated tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/SelectorExpressionBuildSemanticsTests.cs`
5. Extended de-hack guard:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

## Wave 11 (implemented): Property-predicate resolution contract normalization in selector semantics

Deliverables:
1. Remove remaining selector `PropertyEquals` expression-assembly responsibility from binder callbacks.
2. Change selector semantics callback from mutable expression refs to typed `propertyExpression/valueExpression` resolver contract.
3. Keep binder callback focused on Avalonia property resolution and value conversion only.

Execution notes:
1. Extended selector semantics contract:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/SelectorExpressionBuildSemantics.cs`
   - Added:
     - `ISelectorExpressionEmitter.EmitPropertyEquals(...)`
     - `SelectorPropertyPredicateResolver` delegate
   - Updated builder flow to emit property selector shape centrally via emitter.
2. Updated Avalonia emitter implementation:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorExpressionEmitter.cs`
   - Added `EmitPropertyEquals(...)`.
3. Updated binder callback to value-resolution-only role:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - Replaced mutable callback with `TryResolveSelectorPropertyPredicateExpressions(...)`.
4. Updated selector semantics tests for new contract:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/SelectorExpressionBuildSemanticsTests.cs`

## Wave 12 (implemented): Avalonia selector property-predicate adapter extraction

Deliverables:
1. Move remaining selector property-predicate parsing/resolution wiring from binder into a dedicated Avalonia adapter helper file.
2. Keep binder callback as thin orchestration with typed delegates.
3. Keep selector semantics and diagnostics behavior unchanged.

Execution notes:
1. Added dedicated Avalonia predicate resolver adapter:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorPropertyPredicateResolver.cs`
   - Includes typed delegate contracts and `TryResolve(...)`.
2. Updated binder to delegate predicate resolution:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - `TryResolveSelectorPropertyPredicateExpressions(...)` now wraps typed delegates and calls `AvaloniaSelectorPropertyPredicateResolver.TryResolve(...)`.
   - Removed binder-local predicate parse helper now covered by adapter.
3. Updated de-hack guard layering checks:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

## Wave 13 (implemented): Selector semantic adapter consolidation

Deliverables:
1. Move remaining selector wrapper helpers out of binder into a dedicated adapter facade.
2. Make binder consume adapter entrypoints for selector type checks, selector expression build, selector type-token extraction, and selector target-type resolution.
3. Update de-hack guard to assert new layering boundary (`binder -> AvaloniaSelectorSemanticAdapter -> shared semantics/parsers`).

Execution notes:
1. Added adapter facade:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorSemanticAdapter.cs`
   - Includes:
     - `IsSelectorType(...)`
     - `TryBuildSelectorExpression(...)`
     - `TryExtractSelectorTypeToken(...)`
     - `TryResolveSelectorTargetType(...)`
2. Updated binder callsites:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - Replaced direct selector wrappers with `AvaloniaSelectorSemanticAdapter` calls.
   - Removed obsolete binder-local selector wrapper methods.
3. Updated guard tests to enforce adapter boundary:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

## Wave 14 (implemented): Selector conversion context de-localization

Deliverables:
1. Remove remaining selector conversion local functions from binder conversion flow.
2. Move selector conversion context threading (`Compilation`, `XamlDocumentModel`) into selector adapter contracts.
3. Keep binder selector conversion branch as method-group orchestration only.

Execution notes:
1. Extended selector predicate resolver contracts to carry compilation/document context:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorPropertyPredicateResolver.cs`
   - Updated:
     - `TryResolveSelectorPropertyReference`
     - `TryConvertSelectorTypedValue`
     - `TryResolve(...)`
2. Updated selector semantic adapter to accept and forward semantic context:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorSemanticAdapter.cs`
   - `TryBuildSelectorExpression(...)` now accepts `Compilation` and `XamlDocumentModel`.
3. Added binder-level adapter method-group bridges:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - Added:
     - `ResolveSelectorTypeToken(...)`
     - `ResolveWildcardSelectorType(...)`
     - `TryResolvePropertyReference(...)`
     - `TryConvertSelectorTypedValue(...)`
4. Selector conversion branch now calls adapter with method groups only; binder-local selector conversion lambdas removed.

## Wave 15 (implemented): Mechanical binder split for selector-property reference bridge

Deliverables:
1. Convert monolithic binder class to partial type to enable incremental file-level decomposition.
2. Move selector/property-reference bridge methods out of the main binder file into a dedicated partial file with no behavior changes.
3. Preserve selector conversion contracts and property-reference resolution behavior used by style setter conversion and selector predicate paths.

Execution notes:
1. Converted binder declaration to partial:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
2. Added dedicated partial file:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.SelectorPropertyReferences.cs`
3. Moved methods from monolithic file into the new partial:
   - `ResolveSelectorTypeToken(...)`
   - `ResolveWildcardSelectorType(...)`
   - `TryResolvePropertyReference(...)`
   - `TryConvertSelectorTypedValue(...)`
   - `TryResolveAvaloniaPropertyReferenceExpression(...)` overloads
4. Validation completed:
   - full solution build,
   - selector/de-hack/generator focused tests,
   - full test suite.

## Wave 16 (implemented): Mechanical binder split for markup/static parse helper cluster

Deliverables:
1. Move markup-extension conversion helper methods out of the main binder file into a dedicated partial file.
2. Move shared static parse/property/static-member helper methods used by conversion paths into the same helper partial.
3. Preserve conversion behavior and diagnostics unchanged (mechanical split only).

Execution notes:
1. Added new partial file:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.MarkupHelpers.cs`
2. Moved methods from monolithic binder file:
   - `TryConvertXamlPrimitiveMarkupExtension(...)`
   - `TryConvertGenericMarkupExtensionExpression(...)`
   - `TryConvertMarkupArgumentExpression(...)`
   - `TryResolveMarkupExtensionType(...)`
   - `WrapWithTargetTypeCast(...)`
   - `TryConvertByStaticParseMethod(...)`
   - `IsCultureAwareParseParameter(...)`
   - `IsAvaloniaPropertyType(...)`
   - `TryResolveStaticMemberExpression(...)`
3. Removed the moved method bodies from:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
4. Validation completed:
   - full solution build,
   - focused binder/generator/fluent parity tests,
   - full test suite.

## Wave 17 (implemented): Mechanical binder split for static-resource + C# expression-markup helpers

Deliverables:
1. Move static-resource requirement graph walkers from the monolithic binder file into dedicated partial.
2. Move C# expression-markup parse/gate/build helpers into the same partial.
3. Keep behavior deterministic and unchanged; update source guard tests to account for partial-file layout.

Execution notes:
1. Added new partial file:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ExpressionMarkup.cs`
2. Moved methods from monolithic binder:
   - `RequiresStaticResourceResolver(...)`
   - `HasStaticResourceResolverRequirement(...)`
   - `TryConvertCSharpExpressionMarkupToBindingExpression(...)`
   - `TryParseCSharpExpressionMarkup(...)`
   - `IsImplicitCSharpExpressionMarkup(...)`
   - `LooksLikeMarkupExtensionStart(...)`
   - `TryBuildCompiledExpressionAccessorExpression(...)`
   - `TryBuildExpressionBindingRuntimeExpression(...)`
   - `BuildStringArrayLiteral(...)`
3. Updated guard tests to read binder partial set instead of a single source file:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
4. Validation completed:
   - full solution build,
   - focused de-hack/generator/fluent parity tests,
   - full test suite.

## Wave 18 (implemented): Mechanical binder split for include binding and URI normalization helpers

Deliverables:
1. Move include binding/orchestration methods from monolithic binder file into dedicated partial.
2. Move include URI normalization/path-resolution helpers into the same partial.
3. Preserve include diagnostics and include URI resolution behavior unchanged (mechanical split only).

Execution notes:
1. Added new partial file:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.Includes.cs`
2. Moved methods from monolithic binder file:
   - `BindIncludes(...)`
   - `ResolveIncludedBuildUri(...)`
   - `NormalizeIncludeSourceForResolution(...)`
   - `GetIncludeDirectory(...)`
   - `CombineIncludePath(...)`
   - `NormalizeIncludePath(...)`
3. Removed moved method bodies from:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

## 4. Risk Controls

1. Preserve public binder entrypoint and diagnostics IDs.
2. Keep extracted services static/pure where possible.
3. Add focused tests for reusable service and run expression-focused generator tests.

## 5. Validation Matrix

Required in this wave:
1. Build passes for updated projects.
2. Reusable service unit tests pass.
3. Expression generator tests pass.
