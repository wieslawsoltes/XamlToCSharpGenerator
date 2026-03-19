---
title: "Preview and Inspector"
---

# Preview and Inspector

The VS Code design workflow is split into two surfaces:

- the preview editor tab is the design surface
- the `AXSG Inspector` activity-bar container is the companion inspection and editing surface

The inspector is not embedded into the preview editor. It lives in a dedicated left-side rail container, similar to other VS Code side panels.

## Preview compiler behavior

The extension supports three preview startup modes:

- `auto`
- `sourceGenerated`
- `avalonia`

`auto` is the recommended mode and the default. It prefers the AXSG source-generated preview first and falls back to the Avalonia/XamlX host when needed.

## What source-generated preview adds

When the AXSG runtime output is available, the source-generated preview path is the most complete one for AXSG-specific features. Current support includes:

- live unsaved XAML overlay updates
- inline C# evaluation
- expression binding evaluation
- keyboard input forwarding in interactive mode
- `Design.PreviewWith`
- authored design sizing such as `d:DesignWidth` and `d:DesignHeight`
- parity-oriented preview rewriting for AXSG preview-time markup/runtime helpers

## Preview toolbar

The preview tab includes:

- zoom out
- reset zoom
- zoom in
- zoom label
- workspace mode selector
- hit-test/tree selector

## Workspace modes

### `Interactive`

Normal app interaction mode.

- mouse and keyboard input are forwarded to the previewed app
- overlay hit testing is inactive

### `Design`

Designer/selection mode.

- preview clicks perform hit testing instead of normal control interaction
- hover shows overlay feedback
- selection is synchronized back into the inspector trees and the XAML editor

### `Agent`

Reserved for parity with the shared Hot Design workspace model. Keep using `Interactive` or `Design` unless you have a specific agent-driven workflow.

## Hit-test modes

The preview and inspector currently support:

- `Logical`
- `Visual`

This controls how preview hit testing resolves the selected element. The inspector still exposes both `Logical Tree` and `Visual Tree` at the same time.

## AXSG Inspector side panel

The inspector activity-bar container currently exposes:

- `Documents`
- `Toolbox`
- `Logical Tree`
- `Visual Tree`
- `Properties`

### Documents

Shows the design documents in the current workspace-backed preview session and lets you switch the active design document.

### Toolbox

Shows insertable controls/items exposed by the runtime design workspace. Selecting an item inserts it through the runtime mutation pipeline instead of by ad-hoc text generation in the extension.

### Logical Tree and Visual Tree

These show separate live-tree projections from the current preview root.

- selecting a tree item selects the live/source element
- tree expansion state is remembered in workspace state
- selection is synchronized with preview hit testing and the editor caret

### Properties

The properties panel is a webview-based inspector over runtime property metadata and currently supports:

- categorized property groups
- `Smart` vs `All` filter modes
- direct property apply/remove actions
- quick-set buttons
- read-only/enum-aware rendering from the runtime property metadata

Property updates go through the runtime hot-design mutation contract and are applied back to the XAML editor as minimal diffs.

## Selection synchronization

The current synchronization model is:

- preview click in `Design` mode selects through runtime hit testing
- preview hover updates the overlay without mutating editor selection
- tree selection reveals the source element in the editor
- editor caret movement selects the deepest matching source element
- property edits and toolbox insertions update the buffer, then preview/inspector state converges from the refreshed session

## Undo and redo

The inspector toolbar exposes:

- `AXSG Inspector: Undo`
- `AXSG Inspector: Redo`

These operate on the preview-backed design session, not a separate side database.

## Related docs

- [Configuration](configuration/)
- [Editing and Navigation](editing-and-navigation/)
- [Troubleshooting](troubleshooting/)
- [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/)
