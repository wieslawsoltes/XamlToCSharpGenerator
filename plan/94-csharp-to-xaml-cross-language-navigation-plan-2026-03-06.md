# C# To XAML Cross-Language Navigation Plan

Date: 2026-03-06

## Scope

Extend the AXSG VS Code extension and managed language server so C# editor navigation can surface XAML results for CLR symbols referenced from XAML.

Primary user-visible goals:

- `Find All References` on C# types, properties, and methods includes XAML usages.
- `Go To Definition` / `Go To Declaration` from C# can surface XAML declaration-like anchors where that is semantically valid.

Examples:

- C# property used in `{Binding Name}` or `{= Name + "!"}`.
- C# method used in `{= FormatSummary(FirstName, LastName, Count)}`.
- C# type used in `x:DataType`, `AncestorType`, `x:Class`, selector type references, markup extensions.

## Current state

### What already exists

- The extension already activates on `csharp` and contributes a C# code action for cross-language rename.
- The language service already has:
  - Roslyn-backed symbol resolution logic inside rename.
  - Rich XAML reference collection for CLR symbols:
    - type references
    - property references
    - expression-binding symbol references
    - binding path references
    - selector type references
    - type-valued markup extension references
- The language server already supports custom AXSG requests for metadata documents and rename.

### Gaps

- No public language-service API exposes “XAML references for Roslyn symbol at C# position”.
- No server request exists for C# -> XAML references/declarations.
- No VS Code C# definition/reference providers are registered to ask AXSG for XAML locations.
- Rename contains private Roslyn symbol resolution logic that should not be reimplemented ad hoc in multiple places.

## Design constraints

- Do not try to replace or fork the C# extension’s language server.
- Do not duplicate full C# references; AXSG should contribute only XAML locations.
- Keep symbol resolution Roslyn-based and semantic.
- Use the existing XAML semantic collectors; do not add text-only C# -> XAML heuristics.

## Architecture

### 1. Shared Roslyn symbol resolution service

Create a reusable service that:

- resolves a Roslyn `ISymbol` from a C# document URI + position
- respects unsaved editor text via `documentTextOverride`
- returns the symbol plus the `CompilationSnapshot`

This will be the shared base for future cross-language features and can later replace the equivalent private rename helpers.

### 2. C# -> XAML navigation service

Create a service that:

- resolves the C# symbol
- discovers project XAML files
- builds a semantic XAML analysis anchor from an open or on-disk XAML document
- delegates to the existing XAML reference collectors

Supported symbol kinds:

- `INamedTypeSymbol`
- `IPropertySymbol`
- `IMethodSymbol`

Reference behavior:

- Type: include `x:Class`, `x:DataType`, selector type references, markup-extension type references, binding type references, expression symbol usage where applicable.
- Property: include binding path usages, expression bindings, setter-property usages where applicable.
- Method: include expression-binding method usages.

Declaration behavior:

- Type only: return XAML `x:Class` declaration anchors.
- Property/method: return empty because XAML usages are references, not declarations.

## Implementation phases

### Phase A - Shared services

- Add Roslyn symbol resolution service for C# documents.
- Add C# -> XAML navigation service.
- Expose engine methods for:
  - `GetXamlReferencesForCSharpSymbolAsync`
  - `GetXamlDeclarationsForCSharpSymbolAsync`

### Phase B - Server protocol

- Add custom LSP requests:
  - `axsg/csharp/references`
  - `axsg/csharp/declarations`
- Serialize standard LSP locations as the response payload.

### Phase C - VS Code integration

- Register `ReferenceProvider` for `csharp`.
- Register `DefinitionProvider` and `DeclarationProvider` for `csharp`.
- Providers should call AXSG custom requests and return only XAML locations.

## Test plan

### Language service

- C# property symbol -> XAML binding references
- C# method symbol -> XAML expression references
- C# type symbol -> XAML type references
- C# type symbol -> XAML `x:Class` declarations

### Language server

- Custom request `axsg/csharp/references`
- Custom request `axsg/csharp/declarations`

### Extension

- Syntax validation for `extension.js`

## Risks

- Project without any XAML files should return empty results, not errors.
- Symbol resolution must use the same workspace snapshot used for the C# document.
- Multiple providers in VS Code can coexist; AXSG must return only XAML locations to avoid duplicate C# results.

## Acceptance criteria

- `Find All References` from C# on supported CLR symbols returns XAML results in VS Code.
- `Go To Declaration` / `Go To Definition` from C# on view/root types can return `x:Class` XAML anchors.
- No regressions in existing XAML-side references/definitions/rename behavior.
