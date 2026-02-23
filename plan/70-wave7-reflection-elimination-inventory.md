# Wave 7 Reflection Elimination Inventory and Execution Report

Date: 2026-02-22  
Status: Completed (all tracked Wave 7 reflection items closed)

## Scope

Wave 7 from `plan/69-framework-agnostic-refactor-spec-and-plan.md`:

1. Reflection inventory per file with replacement design.
2. Priority hot paths: event binding runtime, hot reload state, runtime loader bridge.
3. Enforce no-reflection in framework-agnostic projects.
4. Document temporary exceptions with target removal dates.

## 1) Inventory (current)

## 1.1 Framework-agnostic projects (`Core`, `Compiler`, `Generator`, `Runtime.Core`)

- Reflection API usage: none in source code.
- Guardrail now enforced by banned API analyzer + tests.

## 1.2 Avalonia adapter/runtime edge (`Runtime.Avalonia`)

Reflection exceptions are explicitly tracked and must match the allowlist in:

- `tests/XamlToCSharpGenerator.Tests/Build/ReflectionGuardTests.cs`

Current exception set:

- none

## 2) Wave 7 actions executed in this slice

## 2.1 Guardrail enforcement

- Added banned API analyzer enforcement in `Directory.Build.props` for:
  - `XamlToCSharpGenerator.Core`
  - `XamlToCSharpGenerator.Compiler`
  - `XamlToCSharpGenerator.Generator`
  - `XamlToCSharpGenerator.Runtime.Core`
- Added `eng/analyzers/BannedSymbols.Reflection.txt`.
- Added build tests in `tests/XamlToCSharpGenerator.Tests/Build/ReflectionGuardTests.cs`.

## 2.2 Runtime compiler de-reflection

- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenRuntimeXamlCompiler.cs` no longer uses reflection lookup/invoke for runtime compile fallback.
- Runtime fallback now calls typed `AvaloniaRuntimeXamlLoader.Load(document, configuration)`.
- Added package reference in `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlToCSharpGenerator.Runtime.Avalonia.csproj`:
  - `Avalonia.Markup.Xaml.Loader`

## 2.3 Event binding hot-path reduction

- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenEventBindingRuntime.cs` now resolves data context through typed `IDataContextProvider` first, with cached property fallback.
- Added regression test coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Runtime/SourceGenEventBindingRuntimeTests.cs`

## 2.4 Markup-extension and hot-design de-reflection closure

- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`
  - Removed runtime assembly/type scanning (`assembly.GetType`/cross-assembly lookup) from binding type resolver.
  - Switched to `SourceGenKnownTypeRegistry` for resolver lookup.
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
  - Removed `Type.GetType`/`assembly.GetType`/`GetFields` paths.
  - Switched element type resolution to `SourceGenKnownTypeRegistry`.
  - Switched property enumeration to `AvaloniaPropertyRegistry`.
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  - Added generated registration of known types through `SourceGenKnownTypeRegistry.RegisterType(typeof(...))` in module initializer, keeping resolver coverage without reflection.

## 3) Temporary reflection exception list (tracked)

Active exceptions:

- none

Closed exceptions:

- `SourceGenMarkupExtensionRuntime` late-bound type resolution  
  Closed on: 2026-02-22 (replaced with `SourceGenKnownTypeRegistry` path).
- `XamlSourceGenHotDesignCoreTools` type/property reflection paths  
  Closed on: 2026-02-22 (registry- and AvaloniaPropertyRegistry-based resolution).
- `SourceGenRuntimeXamlLoaderBridge` dynamic-bridge reflection paths  
  Closed on: 2026-02-22 (bridge left in AOT-safe non-reflective mode; no runtime reflection APIs).
- `XamlSourceGenHotReloadStateTracker` member cleanup reflection paths  
  Closed on: 2026-02-22 (typed cleanup descriptor path).
- `XamlSourceGenHotReloadManager` handler/style reflection probes  
  Closed on: 2026-02-22 (typed handlers and no reflection probes).
- `SourceGenEventBindingRuntime` method/command reflection fallback  
  Closed on: 2026-02-22 (typed/no-op compatibility path without reflection APIs).
- `AvaloniaSourceGeneratedXamlLoader` assembly-identity reflection surface  
  Closed on: 2026-02-22 (public `Assembly` API removed; assembly resolved through anchor type/registered-type mapping).

## 4) Next implementation steps (remaining Wave 7)

1. Keep `Runtime.Core`/`Core`/`Compiler`/`Generator` reflection-free under enforced RS0030 guardrail.
2. Keep `Runtime.Avalonia` reflection usage confined to the empty allowlist via `ReflectionGuardTests`.
