# XAML Refactoring System Plan (2026-03-05)

## Goal
Deliver a proper refactoring system for the AXSG XAML VS Code extension and language server that:

1. supports standard XAML rename through LSP,
2. supports cross-language rename between C# and XAML,
3. uses compiler and Roslyn semantics instead of text-only heuristics,
4. is provider-based so additional refactorings can be added without reworking transport or symbol-resolution infrastructure.

## Problem Statement

### Current state
- The language server has navigation and references for many XAML semantic targets.
- The VS Code extension has no refactoring transport, no rename provider, no code actions, and no workspace-edit pipeline.
- C# rename performed by existing C# extensions does not propagate into XAML.

### Root architectural gap
- Navigation answers are read-only and position-based.
- There is no reusable symbol target contract that can produce:
  - `prepareRename`,
  - rename edit sets,
  - cross-file workspace edits,
  - cross-language propagation through Roslyn rename plus XAML updates.

## Constraints
- We must not add heuristic-only rename rules that ignore compiler semantics.
- We must preserve namespace prefixes, attached-property owners, and selector syntax when renaming XAML tokens.
- We must keep the solution extendable for future refactorings such as extract resource, convert attribute to property element, and namespace fixes.

## VS Code Integration Constraint
- Standard LSP rename can fully support XAML documents.
- VS Code does not provide a clean mechanism for this extension to append XAML edits into another extension's built-in C# rename transaction.
- Therefore:
  - XAML-origin rename uses standard `textDocument/prepareRename` and `textDocument/rename`.
  - C#-origin cross-language rename is exposed through an AXSG command/code action that asks the AXSG server to compute a combined C# + XAML workspace edit.

This is the correct, extendable design for VS Code.

## Scope

### In scope
- Refactoring core contracts in `XamlToCSharpGenerator.LanguageService`.
- Workspace-edit model.
- Provider-based refactoring architecture.
- Standard LSP rename for `.xaml` and `.axaml`.
- Cross-language rename command for `.cs`, `.xaml`, and `.axaml`.
- Semantic rename support for:
  - CLR types referenced in XAML:
    - element names,
    - `x:Class`,
    - `x:DataType`,
    - type-valued markup extension arguments,
    - binding type tokens such as `AncestorType`,
    - selector type tokens.
  - CLR properties referenced in XAML:
    - attribute properties,
    - attached properties,
    - setter `Property` values,
    - binding path segments.
  - XAML-local symbols:
    - `x:Name` / `Name` named elements,
    - resource keys referenced by `StaticResource` and `DynamicResource`.
- Tests for engine, server, and extension-facing protocol payloads.

### Out of scope for this slice
- Intercepting or replacing the built-in C# extension rename UX globally.
- Namespace rename, file rename, and folder move propagation.
- Arbitrary method/event-handler refactorings.
- Non-rename refactorings beyond the provider scaffolding and command/code-action surface.

## Architecture

### 1. Refactoring contracts
- Add a language-service workspace edit model:
  - document text edit,
  - grouped workspace edit,
  - optional change annotations later.
- Add a rename service that returns:
  - whether rename is valid at a position,
  - rename placeholder/range,
  - workspace edit for a new name.

### 2. Symbol target resolution
- Introduce a semantic rename target resolver with explicit target kinds:
  - XAML named element,
  - XAML resource key,
  - CLR type,
  - CLR property.
- The resolver must consume the same semantic services already used for definitions/references rather than duplicate token guessing.

### 3. Roslyn bridge
- Extend workspace snapshots so refactoring code can access the Roslyn `Project` and `Solution`, not just `Compilation`.
- For CLR rename:
  - resolve `ISymbol`,
  - run Roslyn `Renamer.RenameSymbolAsync`,
  - compute C# text edits from old vs new solution,
  - add XAML edits computed from AXSG semantic reference search.

### 4. XAML rename occurrence mapping
- Add rename-occurrence mapping that returns exact replacement spans, not just navigation spans.
- This is required because navigation spans are sometimes wider than the symbol name:
  - `vm:MainWindowViewModel`,
  - `Grid.Row`,
  - `ControlCatalog.MainView`.
- Rename mapping must preserve:
  - namespace prefix,
  - attached owner token,
  - selector syntax,
  - full type qualification where required.

### 5. Server and extension transport
- Language server:
  - `textDocument/prepareRename`
  - `textDocument/rename`
  - custom `axsg/refactor/rename` for cross-language command-driven rename
- VS Code extension:
  - standard rename provider for XAML through LSP capability
  - command `AXSG: Rename Symbol Across C# and XAML`
  - code actions in XAML and C# editors that invoke the command

## Delivery Phases

### Phase A: Core contracts and spec
- Add plan/spec.
- Add workspace-edit and rename-result contracts.
- Extend compilation snapshots with Roslyn project access.

### Phase B: Rename engine
- Add semantic rename target resolution.
- Add rename occurrence mapping for local XAML and CLR-backed XAML symbols.
- Add Roslyn rename bridge and diff-to-workspace-edit conversion.

### Phase C: LSP support
- Add `prepareRename` and `rename` handlers.
- Serialize workspace edits in standard LSP shape.

### Phase D: VS Code integration
- Add AXSG cross-language rename command.
- Add code actions for XAML and C# documents.
- Apply server-returned workspace edits client-side.

### Phase E: Validation
- Engine tests for:
  - XAML-local rename,
  - CLR property rename from XAML,
  - CLR type rename from XAML,
  - cross-language rename from C#.
- LSP tests for XAML rename.
- Command payload coverage for custom cross-language rename responses.

## Acceptance Criteria
- F2 rename works in XAML for supported targets and returns a valid LSP workspace edit.
- Renaming a CLR property or type from XAML updates:
  - C# declarations/references through Roslyn rename,
  - XAML references through AXSG semantic rename mapping.
- AXSG command/code action invoked from a C# editor produces a combined C# + XAML workspace edit.
- Attached-property and prefixed-type renames preserve owner/prefix syntax.
- The system is structured around reusable services, not special-case patches in the server.

## Implementation Status
- Phase A: completed
- Phase B: completed
- Phase C: completed
- Phase D: completed
- Phase E: completed
