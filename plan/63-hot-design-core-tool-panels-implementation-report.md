# Hot Design Core Tool Panels Implementation Report

## Summary
Implemented a full SourceGen hot design core-tools layer with runtime APIs and sample UI wiring for:
1. Toolbar state/actions
2. Elements tree/selection
3. Toolbox categories/insertion
4. Canvas state (zoom/form factor/theme)
5. Properties inspection/editing

The implementation is layered on top of existing `XamlSourceGenHotDesignManager` update orchestration and reuses existing apply/wait/fallback semantics.

## Delivered Runtime Surface

### New model contracts
Added:
1. `SourceGenHotDesignWorkspaceMode`
2. `SourceGenHotDesignPanelKind`
3. `SourceGenHotDesignPropertyFilterMode`
4. `SourceGenHotDesignPanelState`
5. `SourceGenHotDesignCanvasState`
6. `SourceGenHotDesignElementNode`
7. `SourceGenHotDesignPropertyQuickSet`
8. `SourceGenHotDesignPropertyEntry`
9. `SourceGenHotDesignToolboxItem`
10. `SourceGenHotDesignToolboxCategory`
11. `SourceGenHotDesignWorkspaceSnapshot`
12. `SourceGenHotDesignPropertyUpdateRequest`
13. `SourceGenHotDesignElementInsertRequest`
14. `SourceGenHotDesignElementRemoveRequest`

### Core tools service
Added:
- `src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotDesignCoreTools.cs`

Capabilities:
1. Workspace snapshots (documents + selected element + properties + toolbox + panel/canvas/mode state).
2. Elements tree extraction from source XAML with stable path ids.
3. Smart/All property panel projection with quick-set metadata.
4. Source-level property edit operation (`set/remove` attribute).
5. Element insert/remove operations.
6. Document undo/redo history with bounded stack size.
7. Canvas and panel state mutation APIs.
8. Toolbox category generation including project controls.

### Tool facade expansion
Updated:
- `src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotDesignTool.cs`

Added typed wrappers for all core tools operations and command-surface extensions (`snapshot`, `set-mode`, `set-property-mode`, `panel-toggle`, `set-zoom`, `select-doc`, `select-element`, `apply-doc`, `set-property`, `undo`, `redo`).

### Runtime option update
Updated:
- `src/XamlToCSharpGenerator.Runtime/SourceGenHotDesignOptions.cs`

Added:
- `MaxHistoryEntries` with clone propagation.

### Manager integration
Updated:
- `src/XamlToCSharpGenerator.Runtime/XamlSourceGenHotDesignManager.cs`

Change:
- `ClearRegistrations()` now resets core-tools workspace state.

## Tests
Added:
- `tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenHotDesignCoreToolsTests.cs`

Coverage includes:
1. Workspace snapshot returns element tree and properties.
2. Property update persists source and supports undo/redo.
3. Insert/remove operations update source XAML.

## Sample Wiring

### SourceGen XAML Catalog Sample
Added:
1. `samples/SourceGenXamlCatalogSample/Pages/HotDesignStudioPage.axaml`
2. `samples/SourceGenXamlCatalogSample/Pages/HotDesignStudioPage.axaml.cs`
3. `samples/SourceGenXamlCatalogSample/ViewModels/HotDesignStudioViewModel.cs`

Updated:
1. `samples/SourceGenXamlCatalogSample/MainWindow.axaml` (new `Hot Design Studio` tab)
2. `samples/SourceGenXamlCatalogSample/ViewModels/MainWindowViewModel.cs`
3. `samples/SourceGenXamlCatalogSample/README.md`

### SourceGen CRUD Sample
Added:
1. `samples/SourceGenCrudSample/HotDesignStudioPage.axaml`
2. `samples/SourceGenCrudSample/HotDesignStudioPage.axaml.cs`
3. `samples/SourceGenCrudSample/ViewModels/HotDesignStudioViewModel.cs`

Updated:
1. `samples/SourceGenCrudSample/MainWindow.axaml` (new `Hot Design Studio` tab)
2. `samples/SourceGenCrudSample/Infrastructure/RelayCommand.cs` (parameter-aware overload)
3. `samples/SourceGenCrudSample/README.md`

## Documentation
Updated:
- `README.md` with core tool-panel API usage examples.

## Validation
Executed successfully:
1. `dotnet build src/XamlToCSharpGenerator.Runtime/XamlToCSharpGenerator.Runtime.csproj -v minimal`
2. `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal`
3. `dotnet build samples/SourceGenCrudSample/SourceGenCrudSample.csproj -v minimal`
4. `dotnet build samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj -v minimal`

Results:
1. Runtime build: success, 0 warnings, 0 errors.
2. Test suite: passed 273, skipped 1, failed 0.
3. Both sample apps: build success, 0 warnings, 0 errors.

## Notes
This delivery intentionally keeps existing hot design apply orchestration as the source of truth and layers panel operations on top of it. This preserves existing update behavior while enabling runtime design tooling and extension points.
