# VS Code XAML Type Hints Plan (2026-03-05)

## Goal
Add Rider-style semantic type hints to the AXSG XAML/AXAML VS Code language service and extension, using the existing compiler/binder semantics instead of editor-only heuristics.

## Scope
- Language service:
  - retain semantic binding results in the analysis result,
  - compute inlay hints from resolved compiled bindings,
  - map hints to exact XML attribute value locations,
  - expose stable hint labels and tooltips.
- LSP server:
  - advertise `textDocument/inlayHint`,
  - answer range-filtered inlay-hint requests.
- VS Code extension:
  - enable XAML/AXAML inlay hints by default,
  - expose AXSG settings for binding type hints and type-name display style,
  - pass the hint configuration into the server at initialization.

## Feature Slice
Phase A:
- Persist `ResolvedViewModel` in `XamlAnalysisResult`.
- Add an inlay-hint contract and service in `XamlToCSharpGenerator.LanguageService`.

Phase B:
- Implement compiled-binding hints:
  - inline label for binding source/result type,
  - tooltip with source type, target property, normalized path, and resolved result type when available,
  - support element property bindings and style/control-theme setter `Value` bindings.

Phase C:
- Expose the feature through LSP and VS Code:
  - `textDocument/inlayHint`,
  - extension settings/defaults,
  - targeted server and engine tests.

## Non-Goals For This Slice
- Full parameter-name hints for markup constructors.
- Non-binding visual hints such as `Thickness`, `Grid.Row`, or constructor argument labels.
- Code actions or hint interaction beyond standard LSP tooltips.

## Acceptance Criteria
- VS Code requests inlay hints for `.xaml` and `.axaml` documents and receives stable results.
- Hints are generated from semantic binding resolution, not text-only guessing.
- Setter bindings and element property bindings produce hints at the binding value location.
- Tests cover semantic-model retention, inlay hint generation, and LSP transport.

## Status
- Phase A: completed
- Phase B: completed
- Phase C: completed
