# Hot Design Core Tool Panels Parity Spec and Plan

## Objective
Deliver a SourceGen-native hot design workspace that reaches practical parity with the reference core panel model:
1. Toolbar
2. Elements panel
3. Toolbox panel
4. Canvas panel
5. Properties panel

This scope extends the existing hot design infrastructure (enable/disable, document registration, apply update) into a full runtime tools surface and sample integration.

## External Baseline (Docs Analysis)
Reference documentation was analyzed for these panel expectations:
1. Toolbar: mode switching, undo/redo, viewport/form-factor controls, and project sync actions.
2. Elements: live hierarchical tree, search/filter, and selection synchronization.
3. Toolbox: categorized control library, searchable insertion surface.
4. Canvas: visual editing viewport with zoom/form-factor context.
5. Properties: smart/all property views, quick-set actions, inline editing.

## Current State in Repository
Existing implementation already provides:
1. Hot design mode toggle and status.
2. Document registration by type/build URI/source path.
3. Apply-update flow (persist source + optional wait for hot reload + runtime fallback).
4. Custom applier extensibility.

Missing for panel parity:
1. Workspace mode/panel/canvas state model.
2. Elements tree modeling and selection API.
3. Toolbox catalog and insertion/removal operations.
4. Properties inspection/edit operations with smart/all filtering and quick-set metadata.
5. Undo/redo history per document.
6. End-user tool panel UI in samples.

## Target Architecture

### 1) Runtime workspace model
Add explicit models for panel-capable hot design sessions:
1. Workspace mode (`Agent`, `Design`, `Interactive`).
2. Panel visibility state (toolbar/elements/toolbox/canvas/properties).
3. Canvas state (zoom + form factor).
4. Element tree nodes.
5. Property entries and filter mode (`Smart`, `All`).
6. Toolbox categories/items.
7. Workspace snapshot aggregate for UI tooling.

### 2) Document state and history
Per registered document, track:
1. Last known XAML text.
2. Undo stack.
3. Redo stack.
4. Revision counters.

History rules:
1. Successful updates append to history unless explicitly suppressed.
2. Undo applies previous snapshot and pushes current into redo.
3. Redo reapplies next snapshot and pushes current into undo.
4. Max history bounded by options.

### 3) Panel operations API
Add manager/tool operations:
1. Workspace state retrieval.
2. Mode and panel toggles.
3. Canvas zoom/form-factor updates.
4. Element selection.
5. Property update operations.
6. Element insert/remove operations.
7. Undo/redo operations.

### 4) XAML model operations
For panel edits, use XML document operations over latest source text:
1. Parse into `XDocument`.
2. Stable element ids via tree path (`0/1/2`).
3. Update attributes for property edits.
4. Insert/remove element nodes.
5. Re-emit XAML and run existing `ApplyUpdateAsync` path.

### 5) Sample integration
Wire a comprehensive Hot Design Studio panel into both sample apps:
1. Toolbar row for mode/undo/redo/zoom/form factor/panel toggles.
2. Elements + Toolbox on left.
3. Canvas/editor center (editable XAML buffer + apply).
4. Properties on right with smart/all filtering and inline edit actions.

## Public API Additions

### Runtime models
1. `SourceGenHotDesignWorkspaceMode`
2. `SourceGenHotDesignPanelKind`
3. `SourceGenHotDesignPropertyFilterMode`
4. `SourceGenHotDesignPanelState`
5. `SourceGenHotDesignCanvasState`
6. `SourceGenHotDesignElementNode`
7. `SourceGenHotDesignPropertyEntry`
8. `SourceGenHotDesignToolboxItem`
9. `SourceGenHotDesignToolboxCategory`
10. `SourceGenHotDesignWorkspaceSnapshot`

### Requests
1. `SourceGenHotDesignPropertyUpdateRequest`
2. `SourceGenHotDesignElementInsertRequest`
3. `SourceGenHotDesignElementRemoveRequest`

### Manager/tool methods
1. Workspace snapshot retrieval.
2. Mode/panel/canvas setters.
3. Selected document/element setters.
4. Property apply API.
5. Element insert/remove API.
6. Undo/redo APIs.

## Non-Goals (This Wave)
1. Visual drag handles or WYSIWYG direct-manipulation overlays.
2. Full design-time layout solver.
3. Arbitrary semantic refactoring of complex markup extension text.

## Implementation Plan

### Wave A: Runtime model + manager surface
1. Add workspace/panel/property/toolbox model types.
2. Extend hot design options with history bounds.
3. Implement workspace state in manager.
4. Implement snapshot generation.

### Wave B: XML operations + history
1. Add element-tree extraction from XAML.
2. Add property edit and element insert/remove operations.
3. Add undo/redo document history.
4. Extend tool facade command/method surface.

### Wave C: Sample UX wiring
1. Add `HotDesignStudioPage` for catalog sample.
2. Add equivalent panel window/page for CRUD sample.
3. Wire to runtime APIs for live state, apply, undo/redo, insert/remove.
4. Update sample docs.

### Wave D: Validation
1. Runtime tests for snapshot/model operations.
2. Runtime tests for property edit + insert/remove + undo/redo.
3. Build tests for both samples.

## Acceptance Criteria
1. Runtime API returns complete workspace snapshot for at least one registered document.
2. Elements panel data shows navigable hierarchical nodes with stable ids.
3. Property updates apply through hot design and persist via existing update flow.
4. Insert/remove operations apply and are undoable/redoable.
5. Both samples expose and use the hot design panel UI.
6. Existing hot design and hot reload tests remain passing.
