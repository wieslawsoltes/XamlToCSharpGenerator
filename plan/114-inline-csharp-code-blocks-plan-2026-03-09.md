# Inline C# in XAML Plan

## Goal

Add a valid-XAML inline C# surface that supports normal C# expressions and event
code with access to the local XAML context, while preserving source-generator,
hot reload, runtime, and language-service behavior.

## Scope

In scope:

- compact `CSharp` attribute form
- object-element `CSharp` code blocks
- value expressions with `source`, `root`, `target`
- event lambdas and event statement blocks
- language-service support for completion, hover, semantic tokens, definitions,
  references, and inlay hints where applicable
- sample page coverage
- regression tests for compiler, runtime, and language service

Out of scope:

- arbitrary code-behind compilation units
- inline code as a standalone visual object
- async event code

## Feature matrix

| Area | Current state | Required change | Validation |
| --- | --- | --- | --- |
| Valid XAML syntax | shorthand-only for many C# cases | add explicit `CSharp` forms | parser and generator tests |
| Value code | expression markup only | add explicit inline code expressions with context variables | generator + sample build |
| Event code | handler names, event binding markup, inline lambdas | add explicit lambda and statement-block code | generator + hot reload regression |
| Runtime | expression binding runtime only | add inline-code binding runtime with `root`/`target` context | runtime tests |
| Parser | trimmed text only | preserve raw text for code elements | parser tests |
| LS completion/navigation | attribute expressions only | support compact/object-element code blocks | LS tests |
| Semantic tokens | attribute expressions only | colorize object-element C# blocks | LS tests |
| Samples | no explicit valid-XAML inline code page | add dedicated sample page | sample build/manual smoke |

## Phases

### Phase 1: Parser and runtime surface

- Add `XamlToCSharpGenerator.Runtime.CSharp`
- Add `XamlToCSharpGenerator.Runtime.CSharpExtension`
- Preserve raw inline text in `SimpleXamlDocumentParser`
- Add helpers that recognize `CSharp` nodes and compact markup

### Phase 2: Compiler value/event binding support

- Add inline-code expression analysis service with explicit context variables
- Add inline-code statement analysis service for event bodies
- Extend binder to translate:
  - compact property code
  - property-element code blocks
  - compact event code
  - event property-element code blocks
- Extend emitter for event statement bodies
- Extend hot-reload expression detection so runtime fallback skips unsupported direct reload paths

### Phase 3: Language service

- Add object-element code block discovery and range mapping
- Add C# semantic analysis for code blocks using the same context model as the compiler
- Wire completion, hover, refs/defs, semantic tokens, and inlay hints

### Phase 4: Samples and regression coverage

- Add a dedicated inline-code catalog page
- Add generator/runtime/LS regression tests
- Validate sample build and full test suite

## Acceptance criteria

- A value property can be assigned with `<CSharp>` and compile successfully
- An event can be handled with `<CSharp>` statement code and compile successfully
- Compact `CSharp` attribute form works for short expressions/lambdas
- The language service provides completion and navigation inside code blocks
- Sample page builds and renders
- Full test suite passes with no new debug-only artifacts
