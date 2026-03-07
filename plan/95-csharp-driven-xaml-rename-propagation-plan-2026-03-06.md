# C#-Driven XAML Rename Propagation Plan
Date: 2026-03-06

## Problem
The AXSG stack already supports cross-language rename when the rename is initiated through AXSG's own XAML/C# rename entrypoints. However, the standard VS Code C# rename flow does not currently propagate symbol renames into XAML usages such as:
- `x:DataType`
- `x:Class`
- binding paths
- expression bindings
- setter properties
- markup-extension type/member usages

This creates a split-brain refactoring experience: C# rename updates CLR code, while XAML remains stale unless the user explicitly runs the AXSG rename command.

## Baseline analysis

### Existing assets
- `XamlRenameService` already computes cross-language rename edits from both XAML and C# entrypoints.
- `CSharpSymbolResolutionService` already resolves Roslyn symbols from C# source locations.
- `CSharpToXamlNavigationService` already provides C# -> XAML references/declarations.
- The VS Code extension already has a custom command `axsg.refactor.renameSymbol` and C# cross-language navigation providers.

### VS Code rename constraint
VS Code rename providers are **not merged** across extensions. The rename controller walks providers in order and uses the first provider that resolves and returns edits. That means AXSG cannot rely on “additive” rename providers to append XAML edits to the built-in C# rename result.

### Required consequence
To preserve the real C# rename semantics while extending them into XAML, AXSG must:
1. invoke the active C# rename provider explicitly,
2. obtain AXSG's XAML propagation edit for the same symbol,
3. merge/apply both edits as one operation.

## Design

### 1. Dedicated XAML-only rename propagation service
Add a dedicated language-service/server path that resolves a C# symbol and returns **only XAML edits** for the requested rename.

Why:
- avoids duplicating C# edits already produced by the real C# provider,
- keeps the contract explicit,
- makes tests precise,
- keeps the extension bridge simple.

### 2. Extension-side rename bridge
The VS Code extension command for rename will branch by document language:
- `xaml` / `axaml`: keep the existing AXSG-driven rename flow.
- `csharp`:
  - call VS Code hidden command `_executePrepareRename`
  - prompt for the new name
  - call VS Code hidden command `_executeDocumentRenameProvider` to get the CLR rename `WorkspaceEdit`
  - call AXSG `axsg/csharp/renamePropagation` to get the XAML-only propagation edit
  - merge the XAML text edits into the native `WorkspaceEdit`
  - apply one combined edit

This preserves the actual C# language service rename behavior, including provider-specific logic, while extending the scope into XAML.

### 3. Standard user entrypoints
Make the integrated AXSG rename reachable from normal editor usage:
- keep the explicit AXSG command in command palette,
- keep/refine C# code action entry,
- add `F2` keybinding override for `csharp`, `xaml`, and `axaml` so the normal rename gesture uses the integrated flow.

Note:
- VS Code context-menu rename can still be served by the built-in command path if the user invokes that exact menu entry outside AXSG keybinding/command routing.
- AXSG cannot universally replace every rename entrypoint without taking ownership of the entire rename provider chain, which is not reliable in VS Code.

## Scope of propagation
The propagation pass must handle all symbol rename targets AXSG already understands in XAML:
- named CLR types
- CLR properties
- CLR methods used by expression bindings
- type references in `x:DataType`, `x:Class`, and other type-valued XAML positions
- binding path members
- expression binding member usages
- setter property tokens and attached-property owner-qualified forms where the renamed CLR member is represented in XAML

## Implementation steps

### Phase A - API and server contract
- Add language-service API for `GetXamlRenamePropagationEditsForCSharpSymbolAsync(...)`.
- Add dedicated server request: `axsg/csharp/renamePropagation`.
- Reuse existing Roslyn symbol resolution and rename propagation logic; do not duplicate binder logic.

### Phase B - Refactoring service split
- Refactor `XamlRenameService` so Roslyn rename generation and XAML propagation generation are separate operations.
- Ensure the new propagation path returns only `.xaml` / `.axaml` edits.
- Keep existing `axsg/refactor/rename` behavior unchanged.

### Phase C - VS Code bridge
- Extend `axsg.refactor.renameSymbol` to use the C# rename bridge for `csharp` documents.
- Add helper to merge AXSG protocol workspace edits into a native `WorkspaceEdit` returned by `_executeDocumentRenameProvider`.
- Add `F2` keybinding for `csharp`, `xaml`, and `axaml`.

### Phase D - Tests
- Language-service tests for XAML-only propagation edits from C#:
  - property rename
  - method rename used in expression binding
  - type rename used by `x:DataType`
- LSP integration tests for `axsg/csharp/renamePropagation`.
- Keep full language-service suite green.

## Risks
- Hidden VS Code commands `_executePrepareRename` and `_executeDocumentRenameProvider` are internal API surface. They are stable enough for current VS Code integrations, but the bridge should fail cleanly and report a clear message if unavailable.
- File rename operations in the native C# `WorkspaceEdit` must be preserved. AXSG should mutate that native edit by appending XAML edits rather than rebuilding it from scratch.

## Acceptance criteria
- Pressing `F2` on a C# property used in XAML updates both C# and XAML.
- Pressing `F2` on a C# method used in expression bindings updates both C# and XAML.
- Pressing `F2` on a C# view-model type used in `x:DataType` updates both C# and XAML.
- Existing AXSG rename from XAML remains unchanged.
- Full language-service tests remain green.
