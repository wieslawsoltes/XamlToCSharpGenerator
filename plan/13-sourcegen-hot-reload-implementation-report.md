# SourceGen Hot Reload Implementation Report

## Scope completed
This execution completes the hot reload plan from:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/11-avalonia-markup-declarative-hot-reload-analysis.md`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/12-sourcegen-hot-reload-spec-and-plan.md`

## Implemented runtime changes
1. Added metadata update handler manager:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotReloadManager.cs`
   - Uses `[assembly: MetadataUpdateHandler(...)]`
   - Implements `ClearCache(Type[]?)` and `UpdateApplication(Type[]?)`
   - Tracks instances with weak references
   - Registers per-type reload delegates
   - Dispatches Avalonia-object reloads through UI dispatcher
   - Exposes `HotReloaded` and `HotReloadFailed` events
2. Extended AppBuilder runtime API:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/AppBuilderExtensions.cs`
   - `UseAvaloniaSourceGeneratedXaml(...)` now enables loader + hot reload
   - Added `UseAvaloniaSourceGeneratedXamlHotReload(this AppBuilder, bool enable = true)`

## Implemented generator/emitter changes
1. Hot reload option already wired in options model:
   - `AvaloniaSourceGenHotReloadEnabled` (default `true`)
2. Emitter hot reload wiring:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
   - Emits `__ApplySourceGenHotReload()` (when enabled)
   - Registers live instances in `InitializeComponent` sourcegen path via:
     `XamlSourceGenHotReloadManager.Register(...)`
3. Idempotent graph re-apply hardening:
   - Emits `__TryClearCollection(object?)`
   - Clears collection/dictionary targets before add operations
   - Rewires events with `-=` then `+=`
   - Clears content (`Content = default!`) when content node has no children

## Implemented test coverage
1. Runtime extension and manager tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/AppBuilderExtensionsTests.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenHotReloadManagerTests.cs`
2. Generator emission tests:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
   - Added assertions for:
     - emitted hot reload apply method
     - emitted hot reload registration call
     - disable switch behavior
     - idempotent clear + event rewire patterns

## Build and validation status
1. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Result: success (0 errors)
2. `dotnet test` execution in this environment
   - Build phase succeeds
   - Test host execution is blocked by sandbox socket permission (`SocketException (13): Permission denied`)

## Notes
1. This implementation is sourcegen-backend specific and does not alter XamlIl backend behavior.
2. UI runtime full verification of hot reload callbacks requires running tests outside the current socket-restricted sandbox.
