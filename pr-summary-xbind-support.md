# PR Summary: Add Full x:Bind Support for Avalonia Source Gen and LSP

This file is intentionally left uncommitted.

## Branch

- `feature/xbind-support`

## Commit Breakdown

1. `eb6959156` `feat: add x:Bind compiler and tooling support`
2. `023d7c7ac` `feat: add x:Bind catalog sample page`
3. `27600fd6d` `chore: bump version to 0.1.0-alpha.21`

## Overview

This change set adds end-to-end `x:Bind` support to the Avalonia source-generation stack and language tooling, then documents and demonstrates the feature in the catalog sample.

The work covers:

- parser support for `x:Bind` expressions and event markup
- semantic binding and code generation for root, template, named-element, static-member, method-call, indexer, and pathless `x:Bind`
- runtime support for generated `x:Bind` expression bindings, including `TwoWay`/`BindBack`
- LSP support for completion, hover, definition/navigation, references, and signature help in `x:Bind` contexts
- sample pages that exercise the supported `x:Bind` surface
- regression coverage for generator, runtime, and LSP behavior

## Main Changes

### 1. Compiler and semantic model

Added framework-neutral and Avalonia-specific support for `x:Bind` in the source-generation pipeline.

- Introduced dedicated `x:Bind` parsing models and parser infrastructure in `XamlToCSharpGenerator.MiniLanguageParsing`.
- Added event-binding parsing and semantic models for `x:Bind` event handlers.
- Implemented Avalonia semantic binding for `x:Bind`, including:
  - root-scope source resolution
  - template-scope source resolution via `x:DataType`
  - named-element access
  - static member access
  - method invocation expressions
  - indexers
  - pathless `{x:Bind}`
  - `x:DefaultBindMode`
  - `BindBack`
  - event handler bindings
- Updated emission so generated Avalonia code routes `x:Bind` through the runtime helpers instead of falling back to reflection binding behavior.

### 2. Runtime support

Added runtime pieces needed by generated `x:Bind` output.

- Added `SourceGenBindingDependency` and related source-kind models for generated dependency tracking.
- Added runtime `ProvideXBindExpressionBinding(...)` support that builds the forward binding graph and the generated bind-back path.
- Updated expression converters so typed source/root/target evaluation works for generated multi-binding based `x:Bind`.
- Added a reentrancy guard in `SourceGenXBindBindBackObserver<TSource>` to stop `TwoWay` bind-back loops when source updates are echoed back through the target binding pipeline.

This specifically fixes the recursive runtime failure observed on the sample `Alias` binding and the explicit `BindBack=ApplySearchDraft` path.

### 3. Language service

Extended the language service so `x:Bind` behaves like a first-class feature in the editor.

- Added `x:Bind`-aware completion and source-type resolution.
- Added definition/navigation and reference lookup for `x:Bind` members.
- Added hover content for `x:Bind` members.
- Added signature help for `x:Bind(...)` parameters.
- Updated completion plumbing so `x:Bind` contexts are routed through the new logic cleanly.

### 4. Sample catalog coverage

Added a dedicated catalog sample page for `x:Bind`.

- New `XBindPage` tab in `SourceGenXamlCatalogSample`.
- Demonstrates:
  - root-scope `x:Bind`
  - named-element access
  - static members
  - method calls
  - indexers
  - `x:DefaultBindMode`
  - direct assignable `TwoWay` `x:Bind`
  - explicit `BindBack`
  - template-scope `x:Bind`
  - pathless `x:Bind`
  - `TargetNullValue` / `FallbackValue`
  - converters
  - `x:Bind` event handlers

### 5. Version bump

Updated the repo/versioned client metadata to `0.1.0-alpha.21`.

## Notable Implementation Details

### Runtime loop fix

The sample exposed a real runtime issue in the generated `TwoWay` `x:Bind` path: bind-back updates could synchronously re-enter the same source setter or callback after the forward binding pushed the value back into the target property store.

The fix is intentionally in the runtime observer layer rather than in the sample or in generated code. That keeps the behavior correct for all generated `TwoWay` `x:Bind` usages.

### Expression support fixes exposed by the sample

While adding the sample, the compiler/runtime path was also tightened to support:

- conditional access inside invocation arguments in generated `x:Bind` expressions
- correct default priority emission for generated `x:Bind`

## Testing and Validation

Validated with:

- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal --filter "FullyQualifiedName~XBind"`
  - Passed: 16
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal --filter "FullyQualifiedName~ProvideXBindExpressionBinding_TwoWay_Root_Assignment_Does_Not_Reenter_BindBack"`
  - Passed: 1
- `dotnet build samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj -v minimal`
  - Succeeded with 0 warnings / 0 errors

Added/updated regression coverage in:

- generator tests
- runtime `x:Bind` tests
- language service engine tests
- LSP integration tests

## Compatibility Notes

- Intended target remains Avalonia `11.3.12`.
- Prior investigation for Avalonia `12.0.0-rc1` still applies: the compiler-side work is portable, but the runtime binding adapter layer is not a drop-in due to upstream binding API changes. A separate Avalonia 12 runtime adapter remains necessary.

## Suggested PR Description

Add full `x:Bind` support to the Avalonia source generator and language service. This introduces parser, semantic binder, emitter, runtime, and editor tooling support for generated `x:Bind`, adds a dedicated sample catalog page covering the feature set, fixes a `TwoWay` bind-back reentrancy loop exposed by the sample, and bumps the prerelease version to `0.1.0-alpha.21`.
