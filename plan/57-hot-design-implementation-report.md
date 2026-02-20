# Runtime Hot Design Implementation Report

## Delivered
Implemented a SourceGen hot design system with compiler/runtime plumbing, runtime tool API, configurability, and applier extensibility.

## Plan Execution Summary

### Wave 1: Compiler and build plumbing
1. Added MSBuild property `AvaloniaSourceGenHotDesignEnabled`:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
2. Added generator option flow:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
3. Added view-model pass-through flag:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedViewModel.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
4. Emission updates:
   - Hot design registration emitted when enabled.
   - `__ApplySourceGenHotReload` + state tracking now emitted when either hot reload or hot design is enabled.
   - File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

### Wave 2: Runtime hot design platform
Added new runtime contracts/types:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignOptions.cs`
2. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignRegistrationOptions.cs`
3. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignDocumentDescriptor.cs`
4. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignUpdateRequest.cs`
5. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignApplyResult.cs`
6. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignStatus.cs`
7. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/ISourceGenHotDesignUpdateApplier.cs`
8. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignUpdateContext.cs`
9. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotDesignManager.cs`
10. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotDesignTool.cs`

Runtime capabilities now include:
1. runtime mode toggle (`Enable/Disable/Toggle`),
2. document registration/tracking by build URI/type/source path,
3. runtime edit apply API (`ApplyUpdate/ApplyUpdateAsync`),
4. source propagation to file system,
5. hot-reload completion wait policy,
6. fallback runtime apply policy,
7. pluggable update appliers via `ISourceGenHotDesignUpdateApplier`.

### Wave 3: AppBuilder integration and docs
1. Added extension:
   - `UseAvaloniaSourceGeneratedXamlHotDesign(...)`
   - File: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/AppBuilderExtensions.cs`
2. Updated docs:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/README.md`
3. Enabled hot-design property and runtime extension usage in samples:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/Program.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/Program.cs`

## Tests Added/Updated
1. Generator coverage:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
   - Added hot-design emit assertions.
2. Runtime manager coverage:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenHotDesignManagerTests.cs`
3. AppBuilder coverage:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/AppBuilderExtensionsTests.cs`

## Validation
Executed successfully:
1. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal`
   - result: success, `0` warnings, `0` errors.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal --no-build`
   - result: Passed `254`, Skipped `1`, Failed `0`.
3. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj -v minimal`
   - result: success, `0` warnings, `0` errors.
4. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj -v minimal`
   - result: success, `0` warnings, `0` errors.

## Notes
This implementation provides foundational hot design plumbing and tool-invocable runtime APIs, aligned with separation between:
1. update request/apply transport,
2. runtime UI refresh orchestration,
3. extensible per-update policy hooks.
