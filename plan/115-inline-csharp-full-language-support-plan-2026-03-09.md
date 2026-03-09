# Inline C# Full Language Support Plan

Date: 2026-03-09

## Goal

Provide C#-grade inline editor support for:

- inline code attributes
- inline code object elements
- inline code inside `<![CDATA[ ... ]]>`

The support target is:

- semantic colorization
- completion
- hover
- go to definition
- go to declaration
- find references

All of it must execute against the real project compilation and current XAML scope.

## Constraint

Do not delegate embedded snippets to an external C# language server as isolated virtual files.

Reason:

- embedded snippets need the real project compilation
- inline code also depends on XAML-only context:
  - ambient data type
  - root object
  - target element/property type
  - event delegate signature
- an isolated virtual C# file would lose that context and produce incorrect symbols, especially for project-local types and shorthand bindings

The correct implementation is to host Roslyn-backed inline analysis inside AXSG and expose the results through the existing XAML language-server endpoints.

## Current State

Already implemented:

- inline code parsing and binding
- inline code codegen/runtime path
- basic completion
- basic hover
- external-symbol definition/reference support
- lexical tokenization plus partial semantic overlay

Current gaps:

- lambda parameters are not first-class navigation targets
- local variables declared inside inline code are not first-class navigation targets
- member access completion does not resolve receiver types from locally-declared symbols
- semantic tokens do not classify local variables as semantic symbols
- declarations for authored local symbols in inline snippets stay unresolved
- CDATA uses the same inline path, but local-symbol behavior is still incomplete there too

## Optimization / Implementation Matrix

| Area | Current gap | Implementation | Tests |
| --- | --- | --- | --- |
| Inline analysis model | Only external symbol references survive analysis | Extend snippet analysis to emit symbol occurrences with declaration flags and semantic token kinds | Unit tests for expression/lambda/block symbol extraction |
| Inline navigation | No local declaration target | Resolve inline-local targets directly to XAML ranges | LS definition/reference tests |
| Inline references | Only CLR/XAML declarations are merged | Return same-snippet references for locals/parameters/local functions | LS reference tests |
| Inline completion | Receiver resolution ignores locally-declared identifiers | Resolve receiver type from in-scope local/parameter symbol occurrences | LS completion tests |
| Semantic tokens | Parameters partly work, locals do not | Overlay Roslyn semantic token kinds for locals/parameters/methods/types/properties | LS semantic-token tests |
| CDATA parity | Shares pipeline but lacks dedicated proof | Add CDATA coverage for definitions/references/semantic tokens/completion | LS tests |

## Phases

### Phase 1

Add richer Roslyn snippet analysis results:

- symbol occurrences
- declaration vs usage
- semantic token kind

### Phase 2

Use those occurrences in inline definition/declaration/reference handling:

- local parameter definition
- local variable definition
- local references within the same snippet

### Phase 3

Use the same occurrences for completion:

- local/parameter completions
- member access completion from local receiver types

### Phase 4

Expand semantic token overlay:

- parameter
- variable
- method
- type
- property

### Phase 5

Add CDATA-focused regression tests and run full validation.

## Acceptance Criteria

- `sender.`, `e.`, and user-authored local variables can drive inline completion when their type is known
- go to definition on an authored lambda parameter or local variable stays inside the XAML file
- find references on an authored lambda parameter or local variable returns same-snippet occurrences
- semantic tokens classify local variables and parameters in inline code and CDATA content
- existing inline property/method/type navigation keeps working
- full test suite remains green
