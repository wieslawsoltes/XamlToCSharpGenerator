# 92. Complex Selector Semantics And Language-Service Plan

Date: 2026-03-06

## Scope

Investigate complex Avalonia style selectors such as:

```xaml
<Style Selector="Border.local-card > StackPanel > TextBlock.subtitle">
```

and verify parity across:

1. selector parsing
2. compiler semantics
3. emitter
4. language service navigation/references/rename

The immediate reported failure is that `StackPanel`, `TextBlock`, and `subtitle` inside the selector do not participate correctly in `Find References` / `Go To Definition` / `Go To Declaration`.

## Baseline

### Avalonia selector baseline

Relevant upstream files:

- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup/Markup/Parsers/SelectorGrammar.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlSelectorTransformer.cs`

Key observations:

1. Avalonia parses selectors as a dedicated grammar, not generic XML token text.
2. Child combinators (`>`) are legal mid-selector traversal tokens.
3. Selector traversal does not terminate element parsing; the selector remains attribute text until the closing quote.
4. The transformer consumes every selector syntax node in order and composes the final selector chain structurally.

### Current AXSG baseline

Relevant files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorBranchTokenizer.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorReferenceSemantics.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.ExpressionSemantics/SelectorExpressionBuildSemantics.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlCompletionContextDetector.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlSelectorNavigationService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs`

## Findings

| Area | Current behavior | Avalonia / expected behavior | Finding | Action |
| --- | --- | --- | --- | --- |
| Mini-language tokenizer | `SelectorBranchTokenizer` handles `>`, descendant whitespace, `/template/`, quote/bracket/paren depth correctly | Must preserve combinator structure and not split inside nested text | No root-cause defect found for this selector shape | Keep implementation; add guard test |
| Selector reference enumeration | `SelectorReferenceSemantics` emits type/style-class/pseudoclass references across branch segments | Must expose all selector tokens for tooling | Semantics are correct for `Border.local-card > StackPanel > TextBlock.subtitle`, but lacked dedicated regression coverage | Add parser-level regression test |
| Compiler selector semantics | `SelectorExpressionBuildSemantics` builds combinator chain structurally | Must compose the same chain as Avalonia grammar/transformer | No root-cause defect found for this selector shape | Add explicit complex-chain regression test |
| Emitter | Emitter receives structured combinator callbacks and emits nested selector expressions | Must preserve selector order and target-type flow | No defect found for this reported case | No code change required in this slice |
| LS context detection | `XamlCompletionContextDetector` used raw `LastIndexOf('>')` when locating the current XML tag | `>` inside a quoted selector must not end XML context | Root-cause defect: child combinators inside `Selector="..."` were treated as XML tag terminators | Make tag-start scan quote-aware |
| LS selector target resolution | Navigation originally relied on generic completion token spans | Selector navigation should operate on the exact `Selector` attribute value range | Generic token extraction became unreliable after child combinators | Resolve selector references directly from the `Selector` attribute value range |
| LS reference collection | Reference collection only returned the first matching selector token per attribute | All matching selector tokens in an attribute must be discoverable | Too narrow for repeated or mid-selector matches | Collect all matching selector references |
| LS rename/refactor | Rename reused the same generic token path | Rename must share exact selector reference resolution | Rename on tokens after `>` could fail or resolve the wrong span | Reuse selector-attribute reference resolution in rename |

## Root Cause

The reported `Find References` / `Go To Definition` breakage is primarily a language-service bug, not a compiler/emitter bug.

Specifically:

1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlCompletionContextDetector.cs`
   treated the last `>` before the caret as the end of the current XML tag.
2. In a quoted selector value, `>` is a selector child combinator, not XML syntax.
3. That caused selector tokens after the first child combinator to lose attribute-value context.
4. Navigation, references, and rename then operated on incomplete or invalid context.

## Implementation Plan

### Phase A. Language-service correctness

1. Make XML tag-start detection quote-aware in `XamlCompletionContextDetector`.
2. Resolve selector references from the concrete `Selector` attribute value range instead of generic completion tokens.
3. Update rename to reuse the same selector reference resolver.

Status: completed in this slice.

### Phase B. Reference completeness

1. Change selector reference collection from single-match to multi-match per selector attribute.
2. Cover mid-selector type/class references in engine tests.
3. Cover at least one LSP path for a complex-selector definition request.

Status: completed in this slice.

### Phase C. Guard rails

1. Add parser-level regression test for complex selector reference enumeration.
2. Add compiler-expression regression test for complex child/descendant chain emission.
3. Keep broader language-service selector tests green.

Status: completed in this slice.

## Verification Matrix

| Test layer | Coverage |
| --- | --- |
| Mini-language | complex selector token enumeration |
| Expression semantics | complex selector combinator chain emission |
| Engine | definition/reference behavior for mid-selector type and trailing class |
| LSP | definition request on complex selector middle type |

## Remaining Follow-Up

1. Extend the same exact-range selector resolution to hover and future inlay hints for selector tokens.
2. Add more selector-list coverage for comma-separated branches with mixed combinators.
3. Add explicit coverage for `/template/` navigation tokens where target-type context changes across template scope.
