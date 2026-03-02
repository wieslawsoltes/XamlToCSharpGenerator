# Style Selector Parity Analysis and Fix Plan (2026-02-28)

## Scope
Compare selector parsing and selector expression emission in this repository against Avalonia 11.3.12 selector behavior, then close parity gaps in parser + emitter pipeline.

## Baseline References
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup/Markup/Parsers/SelectorGrammar.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup/Markup/Parsers/SelectorParser.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlSelectorTransformer.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Base/Styling/Selectors.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Base/Utilities/IdentifierParser.cs`

## Current Pipeline (This Repo)
- Tokenization: `src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorBranchTokenizer.cs`
- Syntax validation: `src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorSyntaxValidator.cs`
- Selector expression build: `src/XamlToCSharpGenerator.ExpressionSemantics/SelectorExpressionBuildSemantics.cs`
- Avalonia emission adapter: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorExpressionEmitter.cs`
- Predicate resolve/type conversion: `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorPropertyPredicateResolver.cs`

## Findings
| ID | Area | Gap | Impact |
|---|---|---|---|
| SSP-001 | Target type inference | Branch target type token is not reset after combinators. For selectors like `Button > .x`, inferred target type becomes `Button` instead of unknown/null. | Incorrect style target-type inference, false property-resolution behavior, potential wrong diagnostics. |
| SSP-002 | Grammar parity | `^` nesting is only recognized as a segment prefix in validator/builder, but Avalonia grammar allows nesting token in middle-state too. | Valid selectors can be rejected or emitted incorrectly. |
| SSP-003 | Grammar parity | Unknown pseudo-functions with argument lists (`:foo(...)`) are accepted by validator but not by Avalonia grammar. | Invalid selectors can bypass syntax diagnostics and fail later in conversion/emission path. |
| SSP-004 | Build/validate coherence | Builder can succeed on shapes that should be syntactically invalid (for example property-only selector branch under fallback context), because builder does not gate on validator result. | Divergence between style validation and property conversion path; non-deterministic diagnostics. |
| SSP-005 | Nesting semantics | Builder currently allows nesting selector emission without any nesting context hint. Avalonia XAML transform rejects unresolved parent nesting context. | Runtime-invalid selector graphs can be emitted from compile path. |
| SSP-006 | Lexer parity | Identifier-part rules are narrower than Avalonia `IdentifierParser` (missing Unicode category support beyond letter/digit/underscore). | False negatives for valid unicode identifiers. |
| SSP-007 | nth-child parity | `nth-child` parser strips only `' '` spaces, not all Unicode whitespace handled by Avalonia parser via `SkipWhitespace`. | False negatives for otherwise valid whitespace formatting. |
| SSP-008 | Grammar parity | `*` wildcard branch handling exists in current parser/builder flow, but Avalonia selector grammar does not define wildcard token support. | Non-parity behavior and unexpected acceptance paths. |
| SSP-009 | Grammar parity | `/template/` combinator matching was case-insensitive in tokenizer, while Avalonia grammar requires literal `template` token. | Non-parity acceptance of invalid selectors (`/Template/`). |
| SSP-010 | Predicate lexical parity | Predicate splitter trimmed property/value segments and allowed whitespace around property token; Avalonia parser treats property token lexically strict and preserves raw value segment. | Silent semantic drift for property selectors and typed conversions. |

## Emitter Comparison
| Avalonia selector op | Avalonia reference path | This repo emitter | Status |
|---|---|---|---|
| `OfType` | `Selectors.OfType(previous, type)` | `EmitOfType` | Parity |
| `Is` | `Selectors.Is(previous, type)` | `EmitIs` | Parity |
| `.class` / `:pseudo` | `Selectors.Class(previous, value)` | `EmitClass` / `EmitPseudoClass` | Parity |
| `#name` | `Selectors.Name(previous, name)` | `EmitName` | Parity |
| descendant | `Selectors.Descendant(previous)` | `EmitDescendant` | Parity |
| child | `Selectors.Child(previous)` | `EmitChild` | Parity |
| template | `Selectors.Template(previous)` | `EmitTemplate` | Parity |
| nesting `^` | `Selectors.Nesting(previous)` | `EmitNesting` | Parity |
| `:not(...)` | `Selectors.Not(previous, argument)` | `EmitNot` | Parity |
| `:nth-child` | `Selectors.NthChild(previous, step, offset)` | `EmitNthChild` | Parity |
| `:nth-last-child` | `Selectors.NthLastChild(previous, step, offset)` | `EmitNthLastChild` | Parity |
| `[prop=value]` | `Selectors.PropertyEquals(previous, property, value)` | `EmitPropertyEquals` | Parity |
| OR branches | `Selectors.Or(...)` | `EmitOr` | Parity |

## Implementation Plan
1. Fix parser/validator parity:
- Reset branch target type token on combinators and nesting token.
- Accept `^` in token stream (not only prefix position).
- Reject unsupported pseudo-function calls with arguments.
- Align identifier-part lexical rules with Avalonia identifier parser categories.
- Align nth-child whitespace handling.

2. Fix expression builder parity:
- Enforce syntax-validation gate before expression build.
- Add middle-position nesting token handling in builder.
- Require nesting context hint for nesting emission.

3. Keep emitter contract aligned:
- Preserve call shapes against `Avalonia.Styling.Selectors` methods and ensure no ad-hoc selector string parsing in emitter.

4. Add guard tests:
- Branch target type reset after combinator and nesting.
- Middle-position nesting acceptance.
- Unknown pseudo-function with args rejection.
- Build path rejection for syntactically invalid selectors.
- Identifier Unicode and nth-child whitespace parity tests.
- Template combinator case-sensitivity.
- Predicate lexical strictness and raw value preservation.

## Completed Changes
- Parser/validator:
  - Reset branch target token on combinators and nesting.
  - Added middle-position nesting token support.
  - Reject unsupported pseudo-functions with arguments.
  - Enforced `/template/` case-sensitive matching.
  - Removed wildcard branch handling.
  - Tightened predicate property-token lexical behavior and preserved raw predicate value segment.
  - Aligned identifier categories and nth-child whitespace handling with Avalonia.
- Builder:
  - Added syntax-validation gate.
  - Required nesting context hint for nesting emission.
  - Removed wildcard plumbing.
- Adapter/binder:
  - Removed unused wildcard selector resolver path.
- Tests:
  - Added parser and builder guard tests for all parity fixes listed above.

## Validation
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`
  - Passed: 858 / Skipped: 1 / Failed: 0 (post-parity pass)

## Acceptance Criteria
- Selector validation/build behavior matches Avalonia grammar and transform expectations for covered scenarios.
- No regressions in existing selector-related tests.
- New tests codify the fixed parity contracts.
