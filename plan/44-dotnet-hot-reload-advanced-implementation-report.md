# DotNet Hot Reload: Advanced Implementation Report

Date: 2026-02-20

Implements:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/43-dotnet-hot-reload-advanced-analysis-and-spec.md`

## Summary of Delivered Changes

## 1) Runtime contract and phased extension surface
Added:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotReloadTrigger.cs`
2. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotReloadUpdateContext.cs`
3. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/ISourceGenHotReloadHandler.cs`
4. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotReloadHandlerAttribute.cs`
5. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotReloadRegistrationOptions.cs`

Implemented:
1. Phased hot reload callbacks with per-element handling support.
2. Assembly-level hot reload handler registration attribute.
3. Registration options for before/capture/restore/after callbacks.

## 2) Runtime manager pipeline v2
Updated:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotReloadManager.cs`

Implemented:
1. Serialized reload gate with pending update queue.
2. Replacement-type to original-type mapping via `MetadataUpdateOriginalTypeAttribute`.
3. Pipeline lifecycle events:
   - `HotReloadPipelineStarted`
   - `HotReloadPipelineCompleted`
4. Handler diagnostics event:
   - `HotReloadHandlerFailed`
5. Assembly-level handler discovery and activation.
6. UI-thread pipeline execution for Avalonia object instances.
7. Built-in `StyledElement` DataContext preservation handler.

Preserved:
1. Existing metadata callbacks (`UpdateApplication`, `ClearCache`).
2. IDE polling fallback and source-path watcher behavior.
3. Existing enable/disable entry points.

## 3) AppBuilder integration
Updated:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/AppBuilderExtensions.cs`

Added:
1. `UseAvaloniaSourceGeneratedXamlHotReloadHandler(...)` for app-level handler registration.

## 4) Generated hot reload registration hardening
Updated:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

Changed:
1. Generated hot reload registration now uses `SourceGenHotReloadRegistrationOptions`.
2. Captures/restores `DataContext` during reload to improve runtime state stability.

## 5) Tests added/updated
Updated:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenHotReloadManagerTests.cs`
2. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/AppBuilderExtensionsTests.cs`
3. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Added:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/HotReloadAssemblyHandlerSupport.cs`

New runtime coverage includes:
1. Replacement-type mapping behavior.
2. Reentrant queue replay behavior.
3. Registration state transfer + phased handler lifecycle.
4. Pipeline context event verification.
5. Assembly-level handler activation.
6. AppBuilder handler wiring path.

## Validation Results
1. Runtime-focused test slice:
   - Passed (`23`), Failed (`0`), Skipped (`0`).
2. Generator/build hot-reload related slice:
   - Passed (`8`), Failed (`0`), Skipped (`0`).
3. Full test project:
   - Passed (`204`), Failed (`0`), Skipped (`1`).

## Notes
1. This wave focuses on in-process runtime orchestration and hot reload resiliency in the existing IDE/runtime model.
2. Reflection-based assembly handler loading is optional and scoped to explicit assembly attributes.
