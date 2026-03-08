# XAML C# Expressions Remaining Features Plan

## Scope

This plan covers the remaining gaps between the current implementation and the referenced XAML C# expressions specification for:

- shorthand expressions such as `{Name}` and `{User.DisplayName}`
- page-member and binding-context disambiguation
- event lambda restrictions
- diagnostics and ambiguity handling
- language-service parity for navigation, references, hover, completion, semantic tokens, and diagnostics

This document intentionally uses neutral terminology and does not name the originating framework.

## Current State Summary

The current implementation already covers a substantial part of the expression feature set:

- explicit expressions: `{= ...}`
- implicit complex expressions: `{!Flag}`, `{A && B}`, `{Price * Quantity}`, `{$'{Value:F2}'}`
- inline event lambdas: `{(s, e) => Count++}`
- operator aliases such as `AND`, `OR`, `LT`, `GT`, `LTE`, `GTE`
- single-quoted string normalization and interpolation hole formatting
- language-service support for explicit and implicit expressions, lambda bodies, references, definitions, completion, inlay hints, hover, and semantic tokens

The main remaining gap is semantic resolution for *simple shorthand* expressions. Today, shorthand generally flows through the generic expression-binding runtime path instead of following the specification's precedence model:

1. markup extension match
2. binding-context path binding
3. page/view instance member capture
4. static type/member access

That difference blocks correct two-way behavior, conflict diagnostics, and precise editor semantics.

## Evidence From Current Code

### Compiler

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ExpressionMarkup.cs`
  - `TryConvertCSharpExpressionMarkupToBindingExpression(...)` turns all recognized expression markup into `ProvideExpressionBinding<TSource>(...)`.
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/CSharpSourceContextExpressionBuilder.cs`
  - rewriting is source-parameter centric; it does not model page/view members and binding-context members as separate resolution spaces.
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/CSharpMarkupExpressionSemantics.cs`
  - lexical recognition exists for `this.` and `BindingContext.`, but the semantic pipeline does not implement the full disambiguation contract yet.
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  - inline event lambdas are supported, but async-lambda rejection is not modeled as a dedicated rule.

### Language Service

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Parsing/XamlCSharpMarkupExpressionService.cs`
  - expression detection is now context-aware, but still expression-centric rather than shorthand-resolution-centric.
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlExpressionBindingNavigationService.cs`
  - navigation is strong for explicit and complex expressions, but simple shorthand does not yet surface binding/page/static precedence semantics.
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/SemanticTokens/XamlSemanticTokenService.cs`
  - semantic tokenization is present for expressions, but not yet classified by shorthand resolution kind.

## Gap Matrix

| Area | Spec Expectation | Current State | Gap | Required Work |
|---|---|---|---|---|
| Simple shorthand binding | `{Name}` and `{User.Name}` become real path bindings when the target is bindable and the path resolves against `x:DataType` | Currently goes through generic expression binding runtime | Missing binding-path lowering and two-way semantics | Add a dedicated shorthand classifier and lower simple resolvable paths to real binding definitions |
| Page/view local access | `{Title}` and `{GetFormattedDate()}` can bind/capture page/view members after binding-context path resolution fails | No dedicated page/view fallback pipeline for shorthand | Missing local fallback stage | Add second-stage page/view member resolution and captured-value/codegen path |
| Static invocation fallback | Static members are considered only after binding-context and page/view stages | Generic expression path allows static C# expressions, but not spec-order fallback for simple shorthand | Missing precedence-aware static fallback | Add final static-symbol resolution for simple shorthand |
| Markup-extension ambiguity | Bare identifier that matches both markup extension and shorthand should prefer markup extension and emit warning | Context-aware expression detection avoids some false positives but does not emit a dedicated warning | Missing ambiguity diagnostic | Add dedicated shorthand ambiguity diagnostic and fix ordering |
| Binding-context vs page conflict | Bare identifier existing in both scopes should fail until disambiguated | Current pipeline does not model both scopes explicitly for shorthand | Missing conflict diagnostic | Introduce explicit dual-scope resolution and conflict diagnostic |
| `this.` disambiguation | `{this.Foo}` forces page/view scope | Lexically recognized only | Missing semantic implementation | Add explicit page/view scope parser and binder/LS support |
| `.` disambiguation | `{.Foo}` forces binding-context scope | Not implemented | Missing syntax support | Add `.` shorthand parsing, lowering, and LS support |
| `BindingContext.` compatibility | Existing code recognizes `BindingContext.` as an unambiguous start | Partial lexical support only | Undefined semantics | Decide: support as compatibility alias for `.` or reject with diagnostic |
| Simple shorthand two-way | Simple property-path shorthand on a writable bindable target should be two-way-capable | Generic expression binding is one-way | Missing setter-capable lowering | Lower simple path shorthand to real binding with inferred mode |
| Complex on two-way target | Complex expression on writable target should stay one-way and optionally report informational diagnostic | Today it remains expression-based without dedicated diagnostic | Missing guidance diagnostic | Add informational diagnostic when complex shorthand lands on two-way-capable target |
| Async event lambdas | Async lambdas are explicitly unsupported | No explicit prohibition rule | Missing validation/diagnostic | Detect `async` lambdas and emit dedicated diagnostic |
| Property-element / CDATA expressions | Same semantics should work in attribute and property-element text forms | Attribute path is primary; property-element/CDATA parity is incomplete | Missing parser and LS parity | Expand expression extraction to property-element text nodes and CDATA |
| LS definition/references for shorthand binding paths | `{Name}` should navigate as a binding-path member when lowered as binding | Current LS treats shorthand mainly as generic expression C# | Missing resolution-kind aware navigation | Add shorthand resolution metadata and use it across nav/ref engines |
| LS hover/completion for `this.` and `.` | Scope-specific member sets and hover docs | Not implemented | Missing editor semantics | Add context-aware completion/hover models for explicit scopes |
| LS semantic highlighting | Distinguish binding-path members, page/view members, static types, lambda params, keywords, and punctuation | Expression highlighting exists but not by shorthand resolution kind | Missing richer token classification | Extend semantic-token model with shorthand scope kind |

## Testing Matrix

Before each implementation phase, add or extend focused tests. No phase should land without direct coverage.

| Phase | Required Tests |
|---|---|
| Shorthand binding lowering | Generator tests for `{Name}`, `{User.Name}`, nested writable paths, generated binding mode, emitted runtime shape |
| Page/view fallback | Generator tests for page property, page method, static member fallback order |
| Ambiguity/conflict diagnostics | Generator diagnostics for markup-extension ambiguity, dual-scope conflict, unresolved shorthand |
| Disambiguation prefixes | Generator + LS tests for `{this.Foo}`, `{.Foo}`, and compatibility policy for `BindingContext.Foo` |
| Event lambda restrictions | Generator diagnostics for async lambda, unsupported parameter forms |
| Property-element / CDATA | Parser + generator + LS tests for property-element expression text and CDATA |
| LS parity | Completion, hover, inlay, semantic-token, definition, references, rename, and C# cross-language nav tests for all shorthand forms |

## Implementation Phases

### Phase A: Resolution Model Refactor

Introduce a dedicated shorthand-resolution model instead of routing all recognized `{...}` content through generic expression binding.

Add:

- `ShorthandExpressionKind`
  - `BindingPath`
  - `PageMember`
  - `StaticMember`
  - `ComplexExpression`
  - `MarkupExtensionAmbiguous`
  - `Unresolved`
- `ShorthandExpressionResolutionResult`
- a binder-side resolution service that evaluates the precedence chain once and returns a stable semantic result used by both compiler and language service

Primary files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/CSharpExpressionClassificationService.cs`
- new resolution service under `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/CSharpMarkupExpressionSemantics.cs`

### Phase B: Simple Binding-Path Lowering

For resolvable shorthand paths on bindable targets:

- lower to real binding metadata instead of generic expression binding
- infer two-way eligibility from property writability / target metadata
- keep complex expressions on the existing expression-binding path

Primary files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ExpressionMarkup.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
- runtime only if new metadata helpers are needed

### Phase C: Page/View and Static Fallback

Implement the remaining shorthand precedence steps for simple expressions:

- page/view instance property
- page/view instance method
- static property/method/type-qualified path

For non-bindable targets, page/view and static results should become direct generated value expressions where safe.

Primary files:

- binder resolution service from Phase A
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/CSharpSourceContextExpressionBuilder.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

### Phase D: Disambiguation and Diagnostics

Implement explicit disambiguation and warnings/errors:

- `{this.Foo}` forces page/view scope
- `{.Foo}` forces binding-context scope
- bare-identifier markup-extension ambiguity warning
- page/view vs binding-context conflict error
- unresolved shorthand error
- complex-on-two-way informational diagnostic
- async lambda event diagnostic

Do not reuse the external diagnostic IDs directly. Add AXSG diagnostics with stable meanings.

Primary files:

- binder resolution service
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/CSharpSourceContextLambdaAnalysisService.cs`

### Phase E: Property-Element and CDATA Parity

Extend extraction so the same shorthand and expression semantics apply in:

- attribute values
- property-element text values
- CDATA-wrapped property-element values

Primary files:

- parser/extraction helpers in core parsing
- LS markup expression parser service
- generator binder entry points for property-element text assignments

### Phase F: Language-Service Semantic Parity

Consume the compiler-style shorthand resolution model in the language service.

Add support for:

- definitions/references based on actual resolution kind
- hover text that distinguishes binding-path, page/view, and static resolution
- completion lists for `{.Foo}` and `{this.Foo}`
- semantic tokens for shorthand scopes and interpolation parts
- rename/refactor propagation for shorthand path members and page/view members

Primary files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Parsing/XamlCSharpMarkupExpressionService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlExpressionBindingNavigationService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlExpressionCompletionService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/SemanticTokens/XamlSemanticTokenService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Hover/XamlHoverService.cs`

### Phase G: Sample and Documentation Completion

Extend the sample page to cover:

- bare shorthand binding paths
- explicit `this.` and `.` disambiguation
- ambiguity and conflict cases with visible diagnostics comments or dedicated tests
- property-element / CDATA variants
- async-lambda rejection sample in tests only, not in sample UI

Update docs to describe the final precedence model and limitations without referencing the upstream framework name.

## Proposed Execution Order

1. Phase A
2. Phase B
3. Phase D diagnostics for the new resolution model
4. Phase C fallback lowering
5. Phase F language-service parity for shorthand semantics
6. Phase E property-element / CDATA parity
7. Phase G sample/docs cleanup

This order minimizes churn because the language service should consume the final shorthand-resolution model rather than duplicate temporary heuristics.

## Acceptance Criteria

The feature set is complete when all of the following are true:

- `{Name}` and `{User.Name}` lower to real bindings when eligible
- simple shorthand on writable targets supports two-way semantics where the path is writable
- `{this.Foo}` and `{.Foo}` both work in compiler and language service
- ambiguity and conflict diagnostics are deterministic and tested
- async event lambdas are rejected with a dedicated diagnostic
- property-element / CDATA shorthand behaves the same as attribute shorthand
- language service provides completion, hover, semantic tokens, references, definitions, and rename behavior consistent with the compiler’s shorthand resolution
- sample coverage exists for the supported surface
