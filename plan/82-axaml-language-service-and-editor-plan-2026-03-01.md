# AXAML Language Service and Editor Plan (2026-03-01)

## Goal
Deliver a production-grade AXAML/XAML language stack built on this repository's source-generator compiler semantics:

1. Standalone language service (LSP) for VS Code and other editors.
2. Rich IntelliSense and diagnostics consistent with source-generator parsing/binding.
3. AvaloniaEdit-based in-app AXAML editor control with diagnostics, hover, completion, symbol outline, and semantic highlighting hooks.

## Source Analysis Baseline

### Compiler/runtime baseline in this repository
- Parser and semantic binding pipeline already exists:
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Framework/AvaloniaFrameworkProfile.cs`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- Diagnostics are already standardized (`AXSG####`) and line/column aware through `DiagnosticInfo`.
- Existing README documents an LSP/tooling surface, but implementation is absent.

### Avalonia compiler architecture references
- XamlIl transformation pipeline and compiler extension ordering:
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`
- Build-time compile entry:
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs`

### Roslyn language-service architecture references
- Capability declaration and feature toggles:
  - `/Users/wieslawsoltes/GitHub/roslyn/src/LanguageServer/Protocol/DefaultCapabilitiesProvider.cs`
- Request execution/queue pattern:
  - `/Users/wieslawsoltes/GitHub/roslyn/src/LanguageServer/Protocol/RequestExecutionQueueProvider.cs`
- Host/server composition:
  - `/Users/wieslawsoltes/GitHub/roslyn/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/LanguageServer/LanguageServerHost.cs`

## Parity Requirements

### Feature parity target (editor UX)
- Completion: element, attribute, attached property, markup extension, enum/resource/name value contexts.
- Hover: element/property/type details and resolved namespace/type identity.
- Definition/references: local symbol navigation (`x:Name`, `x:Key`, handler names, resource references).
- Diagnostics: parser + semantic (`AXSG####`) in real time.
- Document symbols/outline.
- Semantic tokens.
- Incremental sync over LSP.

### Architecture parity target (service quality)
- Shared language-core reused by both LSP transport and in-app editor control.
- Deterministic behavior (same semantic engine as source-generator binder).
- Workspace-aware project resolution (nearest project/solution).
- Bounded request execution and cancellation-aware operations.

## Identified Gaps

1. No shipped language-service project under `src/`.
2. No VS Code client wrapper under `tools/vscode/axsg-language-server`.
3. No AvaloniaEdit control integrated with source-generator language semantics.
4. No reusable request model bridging parser/binder diagnostics and interactive editor operations.

## Delivery Phases

### Phase A: Core contracts and analysis engine
- Add `XamlToCSharpGenerator.LanguageService` library:
  - Document/session store.
  - Workspace/project resolution.
  - Compiler-backed analysis service (parse + semantic bind).
  - IntelliSense services (completion, hover, definitions, document symbols, semantic tokens).
- Add deterministic conversion from `DiagnosticInfo` to language-service diagnostics.

### Phase B: LSP host
- Add `XamlToCSharpGenerator.LanguageServer` executable/tool:
  - `initialize`, `shutdown`, `exit`.
  - `didOpen`, `didChange`, `didSave`, `didClose`.
  - `completion`, `hover`, `definition`, `documentSymbol`, `semanticTokens`.
  - publish diagnostics on open/change/save.

### Phase C: AvaloniaEdit control
- Add reusable Avalonia control:
  - `AxamlTextEditor` based on AvaloniaEdit `TextEditor`.
  - Completion popup integration.
  - Diagnostic rendering (squiggles + hover tooltip).
  - Semantic token colorization hook.
  - Bindable properties for text, file path, workspace root, and language-service session.

### Phase D: VS Code wrapper
- Add `tools/vscode/axsg-language-server`:
  - Activation for `.xaml` and `.axaml`.
  - Launches `axsg-lsp` over stdio.
  - Supports configurable command/args/workspace passing.

### Phase E: Validation and quality bars
- Unit tests:
  - Context-sensitive completion.
  - Local definition mapping for names/resources.
  - Diagnostics projection with line/column correctness.
- Integration smoke:
  - LSP host starts and serves initialize/completion/hover/diagnostics.
  - Avalonia editor control renders and updates diagnostics on text change.

#### Phase E Status (2026-03-01)
- Implemented:
  - Resource-key completion in markup-resource contexts (`StaticResource`/`DynamicResource`) with guard tests.
  - Definition resolution for resource-key references with guard tests.
  - LSP hover integration smoke test (`textDocument/hover`) alongside existing initialize/completion/diagnostics smoke coverage.
  - Headless editor integration assertion that diagnostics converge to empty for valid AXAML after text change.

### Phase F: Incremental sync and request coherence
- Upgrade LSP text synchronization to incremental mode and apply ranged `didChange` patches against tracked open-document text.
- Preserve deterministic behavior for out-of-order changes (stale version guard).
- Keep diagnostics publishing stable for incremental edit flows.

#### Phase F Status (2026-03-01)
- Implemented:
  - LSP capability `textDocumentSync.change` upgraded to incremental (`2`).
  - Per-document state tracking in server (`uri -> text/version`) with close/save/open lifecycle updates.
  - Incremental content-change application for ranged edits.
  - Stale version guard for `didChange` requests.
  - LSP integration test proving range-based incremental edit updates diagnostics.

## Acceptance Criteria

1. LSP project builds and runs as a standalone process.
2. VS Code extension can connect and receive diagnostics/completions/hover.
3. AvaloniaEdit control can host AXAML text and display diagnostics/completion.
4. Completion/hover/diagnostics use source-generator semantic paths (no separate heuristic-only parser path for primary answers).
5. No reflection is introduced in source-generated runtime execution paths (tooling-only reflection remains allowed).

## Implementation Notes

- Language-service operations will prioritize semantic determinism over speculative heuristics.
- Where project-wide symbol resolution is unavailable, service returns stable partial answers and preserves diagnostics rather than guessing.
- Request handling is cancellation-aware and serializes document updates before feature requests to avoid stale state.
