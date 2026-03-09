# Inline C# VS Code C#-Provider Interop Plan

Date: 2026-03-09
Branch: feature/xaml-csharp-shorthand-lambdas

## Goal

Use the existing VS Code C# language service/providers to improve inline C# editing inside XAML while preserving AXSG's XAML-aware semantics.

Scope:
- inline C# in compact markup (`{CSharp Code=...}`)
- inline C# object-element `Code="..."`
- inline C# `<![CDATA[ ... ]]>` element content
- inline event lambdas and statement blocks

Target capabilities delegated through projected C# documents:
- completion
- hover
- go to definition
- go to declaration
- find references (best-effort; fall back to AXSG when projection cannot return useful workspace results)
- semantic coloring via C# semantic tokens mapped back into XAML

Out of scope for this phase:
- rename through projected C# providers
- code actions/refactorings through projected C# providers
- diagnostics delegation; AXSG remains source of truth for inline C# diagnostics

## Feasibility Analysis

Yes, this is viable.

The extension already has:
- a VS Code extension-side layer with custom C# cross-language providers
- virtual document providers for metadata/source-link documents
- AXSG server-side inline C# semantic analysis

The missing piece is a projection bridge:
- AXSG server must expose stable inline-C# projections with source-to-projection offset maps.
- The VS Code extension must host projected virtual C# documents and call built-in C# provider commands on them.
- Returned locations/tokens must be mapped back into the XAML document.

Important observation:
- The C# extension already has explicit support for `virtualCSharp-*` schemes.
- Therefore the AXSG projection URI should use a `virtualCSharp-axsg-inline` scheme instead of a custom non-C# scheme.
- VS Code does not expose the active C# semantic-token legend through provider commands.
- Therefore semantic coloring cannot be safely delegated numerically and remapped back into AXSG's XAML semantic-token legend.

Decision:
- reuse existing C# providers for completion, hover, definition, declaration, and references
- keep AXSG as the semantic-color owner for inline C#

## Architecture

### 1. Server projection model

Add a language-service projection service that converts each inline C# context into a compilable virtual C# document.

Projection requirements:
- deterministic per-inline-context id
- projected C# text
- XAML code range
- projected code range
- context kind (`expression`, `lambda`, `statements`)
- document version coupling for cache invalidation

Projection wrapper patterns:
- expression:
  - `internal static object? __Evaluate(...context...) => <raw code>;`
- event lambda:
  - `internal static <delegateType> __Bind(...context...) => <raw code>;`
- event statements:
  - `internal static void __Execute(...context and event args...) { <raw code> }`

Context parameters must match AXSG inline semantics:
- `source`
- `root`
- `target`
- `sender`
- `e`
- `arg0..argN` where applicable for event blocks

### 2. Extension virtual document host

Add a new text document content provider for:
- `virtualCSharp-axsg-inline`

Responsibilities:
- fetch inline projections from the AXSG server
- cache projected document text by `(xamlUri, version, projectionId)`
- expose projected text as C# documents
- refresh projected docs on XAML changes

### 3. Extension-side delegation middleware

Extend the AXSG language-client middleware for `xaml` and `axaml` documents:
- if caret/range is outside inline C#: keep current AXSG behavior
- if caret/range is inside inline C#:
  - build or fetch projected C# document
  - map XAML position/range to projected C# position/range
  - invoke built-in C# provider commands
  - map results back to XAML or keep external CLR locations as-is
  - if projection result is empty/useless, fall back to AXSG server behavior

Use provider commands where possible:
- `vscode.executeCompletionItemProvider`
- `vscode.executeHoverProvider`
- `vscode.executeDefinitionProvider`
- `vscode.executeDeclarationProvider`
- `vscode.executeReferenceProvider`
- semantic tokens via built-in semantic token provider commands if available; otherwise register an additional extension-side semantic token provider for XAML documents that merges AXSG tokens with projected inline-C# tokens from the C# provider path.

### 4. Result mapping rules

#### Completion
- projection-side completion items are remapped into XAML completion items
- keep label/detail/documentation/kind where possible
- use current XAML inline span as the insertion target
- do not trust projected text edits that target wrapper code outside the raw inline span

#### Hover
- return the projected C# hover result when available
- fall back to AXSG hover when projection returns no result

#### Definition / Declaration
- if projected result points into the virtual wrapper but outside the inline code span, do not surface it directly
- if projected result points to a real source/metadata file, surface it directly
- if projected result points to a local declaration inside the inline code span, map it back into the XAML span
- if projected result becomes empty after filtering, fall back to AXSG definition/declaration

#### References
- use projected provider as a first pass
- map local inline references back into XAML
- keep external real-file locations as-is
- if projection returns no useful locations, fall back to AXSG references

#### Semantic tokens
- AXSG remains the owner of the final XAML token stream
- projected C# semantic tokens are mapped only for the raw inline code range
- token-type mapping is restricted to AXSG-supported generic C# token categories:
  - `keyword`
  - `string`
  - `number`
  - `operator`
  - `type`
  - `method`
  - `property`
  - `parameter`
  - `variable`
- wrapper-only tokens are discarded

## Detailed Implementation Plan

### Phase A. Projection contract
- Add model classes for inline-C# projections.
- Extend `XamlInlineCSharpContext` with the event delegate type.
- Add `XamlInlineCSharpProjectionService`.
- Add engine method:
  - `GetInlineCSharpProjectionsAsync(...)`
- Add LSP endpoint:
  - `axsg/inlineCSharpProjections`

### Phase B. Extension projection host
- Add `virtualCSharp-axsg-inline` content provider.
- Add projection cache keyed by XAML document uri/version.
- Add mapping helpers:
  - XAML position -> projected position
  - projected range -> XAML range
  - projected location -> XAML location when local to the snippet

### Phase C. Completion/hover/definition/declaration/references
- Add middleware handlers in client options.
- Delegate inline spans to projected C# providers.
- Fall back to AXSG for unresolved cases.
- Preserve current AXSG behavior outside inline code.

### Phase D. Semantic-color ownership
- Keep AXSG semantic tokens as the source of truth for inline C# coloring.
- Do not attempt numeric semantic-token merging from the external C# provider unless VS Code exposes the provider legend in a future API.

### Phase E. Validation
- Add server-side unit tests for projection generation and event-delegate projection.
- Add extension-side regression coverage where feasible.
- At minimum validate with:
  - `node --check` on the extension JS
  - full `dotnet test`
  - sample build for `SourceGenXamlCatalogSample`

## Testing Matrix

### Compiler / LS tests
- projection for attribute expression
- projection for attribute inline `CSharp`
- projection for CDATA inline `CSharp`
- projection for event lambda with delegate type
- projection for event statement block
- mapping local inline declaration back to XAML
- mapping context variable definitions back to CLR type locations via AXSG fallback

### Extension behavior checks
- completion at `source.` inside CDATA
- completion inside compact `Code="..."`
- hover on property/method/type inside inline code
- definition on local variable inside inline code
- definition on CLR property/type inside inline code
- references for local symbol in inline code
- references for CLR symbol with AXSG fallback
- semantic coloring for CDATA and compact inline code remains stable through AXSG tokenization

## Acceptance Criteria
- Inline C# editing in XAML gets C#-style completion and hover through projected C# provider calls.
- Definition/declaration works for inline-local symbols and CLR symbols without regressing AXSG fallback behavior.
- References remain functional; if the projected C# provider cannot supply workspace-meaningful results, AXSG fallback still returns the previous behavior.
- Completion, hover, definition, declaration, and references for inline C# are delegated through projected C# documents.
- AXSG remains the semantic-color source for inline C# because VS Code does not expose the external C# provider legend for safe token remapping.
- No regression in surrounding XAML colorization, navigation, or diagnostics.
