# Wave 7 Reflection Elimination Inventory and Execution Report

Date: 2026-02-22  
Status: In progress (execution started)

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

Reflection remains in these files:

- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenRuntimeXamlLoaderBridge.cs`  
  Uses reflection and `Reflection.Emit` to bind Avalonia internal `IRuntimeXamlLoader`.
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadStateTracker.cs`  
  Uses reflection to clear/reset removed members and detach handlers for generated root graphs.
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenEventBindingRuntime.cs`  
  Uses reflection fallback for method/command path dispatch when fully typed compile-time dispatch is not available.
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`  
  Uses reflection for style invalidation/internal update probes and handler activation.
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`  
  Uses runtime type discovery for late-bound runtime-compiled markup extension scenarios.

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

## 3) Temporary reflection exception list (tracked)

These are temporary and confined to `Runtime.Avalonia`.

1. `SourceGenRuntimeXamlLoaderBridge`
   - Reason: Avalonia `AvaloniaXamlLoader.IRuntimeXamlLoader` is internal and requires adapter binding.
   - Target removal date: 2026-05-31.
   - Planned replacement: typed loader registration seam via public Avalonia runtime loader contract (or source-generated compile-time bridge in adapter package).

2. `XamlSourceGenHotReloadStateTracker`
   - Reason: generic state cleanup for removed members/events currently works from string descriptors.
   - Target removal date: 2026-06-30.
   - Planned replacement: generated typed cleanup/restoration delegates per root type, no runtime member lookup.

3. `SourceGenEventBindingRuntime`
   - Reason: runtime fallback for non-simple method paths and non-typed data contexts.
   - Target removal date: 2026-06-30.
   - Planned replacement: emitter-generated typed invokers for method bindings; reflection fallback only for explicit opt-in dynamic mode.

4. `XamlSourceGenHotReloadManager` internal probes
   - Reason: internal style invalidation and rude-edit probes currently use method reflection.
   - Target removal date: 2026-06-30.
   - Planned replacement: public API path or adapter abstraction with compile-time binding.

5. `SourceGenMarkupExtensionRuntime` late-bound type resolution
   - Reason: runtime-compiled XAML fallback may require dynamic type lookup from XML namespace/type name.
   - Target removal date: 2026-07-31.
   - Planned replacement: generated namespace/type map registry per assembly with explicit resolver interfaces.

## 4) Next implementation steps (remaining Wave 7)

1. Replace `SourceGenEventBindingRuntime.InvokeMethod` reflection fallback with emitter-generated typed method invocation paths for validated simple method bindings.
2. Introduce generated cleanup delegates in generated roots and switch `XamlSourceGenHotReloadStateTracker` from member-name reflection to typed descriptors.
3. Move `XamlSourceGenHotReloadManager` reflection probes behind adapter interfaces and remove direct `MethodInfo.Invoke` usage.
4. Keep `Runtime.Core`/`Core`/`Compiler`/`Generator` reflection-free under enforced RS0030 guardrail.
