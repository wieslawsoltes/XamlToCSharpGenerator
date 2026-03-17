---
title: "Runtime MCP Hot Design Control"
---

# Runtime MCP Hot Design Control

Use this guide when you want to drive AXSG hot design from an MCP client against a live Avalonia process.

This is the runtime-attached host surface exposed by `XamlSourceGenRuntimeMcpServer`, not the standalone workspace tool and not the preview host.

## Use this guide when

- you are running the app under `dotnet watch` and need live hot design control
- you want to select documents or elements remotely
- you want to apply XAML, property, or element edits through MCP
- you want focused workspace resources instead of rereading the entire coarse snapshot after every change

## Before you start

You need:

- AXSG hot design enabled in the running app
- the runtime MCP host embedded into that app
- an MCP client connected to the runtime host transport

If you still need to choose the host or wire the transport, start with [MCP Servers and Live Tooling](mcp-servers-and-live-tooling/).

## Hot design tools

### Runtime mode and snapshot tools

- `axsg.hotDesign.enable`
- `axsg.hotDesign.disable`
- `axsg.hotDesign.toggle`
- `axsg.hotDesign.status`
- `axsg.hotDesign.documents`
- `axsg.hotDesign.workspace`

### Selection and workspace controls

- `axsg.hotDesign.selectDocument`
- `axsg.hotDesign.selectElement`
- `axsg.hotDesign.setWorkspaceMode`
- `axsg.hotDesign.setPropertyFilterMode`
- `axsg.hotDesign.setHitTestMode`
- `axsg.hotDesign.togglePanel`
- `axsg.hotDesign.setPanelVisibility`
- `axsg.hotDesign.setCanvasZoom`
- `axsg.hotDesign.setCanvasFormFactor`
- `axsg.hotDesign.setCanvasTheme`

### Edit and history tools

- `axsg.hotDesign.applyDocumentText`
- `axsg.hotDesign.applyPropertyUpdate`
- `axsg.hotDesign.insertElement`
- `axsg.hotDesign.removeElement`
- `axsg.hotDesign.undo`
- `axsg.hotDesign.redo`

## Hot design resources

### Coarse snapshots

- `axsg://runtime/hotdesign/status`
- `axsg://runtime/hotdesign/documents`
- `axsg://runtime/hotdesign/events`

### Focused snapshots

- `axsg://runtime/hotdesign/workspace/current`
- `axsg://runtime/hotdesign/document/selected`
- `axsg://runtime/hotdesign/element/selected`

### Per-build workspace snapshots

The runtime host also publishes one workspace resource per registered hot design document:

```text
axsg://runtime/hotdesign/workspace/by-build-uri/<escaped-build-uri>
```

Example:

```text
axsg://runtime/hotdesign/workspace/by-build-uri/avares%3A%2F%2FMyApp%2FViews%2FMainView.axaml
```

These resources appear and disappear as hot design documents register or unregister. Clients should react to `notifications/resources/list_changed` and then relist resources when they care about per-build snapshots.

## Recommended client model

Use this rule:

- subscribe to status and focused resources
- read per-build workspace resources on demand
- relist resources after `notifications/resources/list_changed`

Recommended baseline subscriptions:

- `axsg://runtime/hotdesign/status`
- `axsg://runtime/hotdesign/events`
- `axsg://runtime/hotdesign/workspace/current`
- `axsg://runtime/hotdesign/document/selected`
- `axsg://runtime/hotdesign/element/selected`

That gives an editor-like client enough live state to:

- know whether hot design is enabled
- follow active document changes
- follow element selection changes
- react to edit application results and failures

without rereading the full workspace tree after every mutation.

## Subscribe-then-mutate flow

### 1. Subscribe to the focused workspace resource

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "resources/subscribe",
  "params": {
    "uri": "axsg://runtime/hotdesign/workspace/current"
  }
}
```

### 2. Select the active document

```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "method": "tools/call",
  "params": {
    "name": "axsg.hotDesign.selectDocument",
    "arguments": {
      "buildUri": "avares://MyApp/Views/MainView.axaml"
    }
  }
}
```

The tool returns the refreshed workspace snapshot directly. The runtime host also publishes `notifications/resources/updated` for the focused workspace resources, so subscribed clients can update incrementally.

### 3. Apply a property edit

```json
{
  "jsonrpc": "2.0",
  "id": 12,
  "method": "tools/call",
  "params": {
    "name": "axsg.hotDesign.applyPropertyUpdate",
    "arguments": {
      "buildUri": "avares://MyApp/Views/MainView.axaml",
      "elementId": "submitButton",
      "propertyName": "Content",
      "propertyValue": "Save changes",
      "persistChangesToSource": true,
      "waitForHotReload": false
    }
  }
}
```

Result shape:

```json
{
  "applyResult": {
    "succeeded": true,
    "message": "Applied hot design update.",
    "buildUri": "avares://MyApp/Views/MainView.axaml",
    "sourcePersisted": true,
    "minimalDiffApplied": true,
    "hotReloadObserved": false,
    "runtimeFallbackApplied": false
  },
  "workspace": {
    "activeBuildUri": "avares://MyApp/Views/MainView.axaml",
    "selectedElementId": "submitButton"
  }
}
```

### 4. Read the focused selected-element resource if needed

```json
{
  "jsonrpc": "2.0",
  "id": 13,
  "method": "resources/read",
  "params": {
    "uri": "axsg://runtime/hotdesign/element/selected"
  }
}
```

The resource payload includes:

- `activeBuildUri`
- `selectedElementId`
- `element`

That makes it cheaper to refresh inspector-like UI than rereading the entire workspace tree.

## Document-text apply flow

Use `axsg.hotDesign.applyDocumentText` when the client owns the edited XAML text directly.

```json
{
  "jsonrpc": "2.0",
  "id": 20,
  "method": "tools/call",
  "params": {
    "name": "axsg.hotDesign.applyDocumentText",
    "arguments": {
      "buildUri": "avares://MyApp/Views/MainView.axaml",
      "xamlText": "<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock Text=\"Live\"/></UserControl>"
    }
  }
}
```

Use this when your client edits the full document buffer.

Prefer `applyPropertyUpdate`, `insertElement`, or `removeElement` when the client is operating structurally and wants AXSG to produce the minimal text diff for that specific mutation.

## Undo and redo

Use:

- `axsg.hotDesign.undo`
- `axsg.hotDesign.redo`

Both return:

- `applyResult`
- refreshed `workspace`

The workspace payload includes:

- `canUndo`
- `canRedo`

so clients can keep toolbar state aligned without separate bookkeeping.

## Canvas and panel controls

These tools all return the refreshed workspace snapshot:

- `axsg.hotDesign.setWorkspaceMode`
- `axsg.hotDesign.setPropertyFilterMode`
- `axsg.hotDesign.setHitTestMode`
- `axsg.hotDesign.togglePanel`
- `axsg.hotDesign.setPanelVisibility`
- `axsg.hotDesign.setCanvasZoom`
- `axsg.hotDesign.setCanvasFormFactor`
- `axsg.hotDesign.setCanvasTheme`

That means a client can treat them as pure mutations plus snapshot refresh, rather than mixing a mutating call with an immediate follow-up workspace read.

## Resource invalidation model

After hot design changes:

- `notifications/resources/updated` is sent for focused resources such as `workspace/current`, `document/selected`, and `element/selected`
- `notifications/resources/list_changed` is sent when the registered document set changes and per-build workspace resources may have appeared or disappeared

Use this rule:

- if the active document or selection changed, trust the updated focused resources
- if the document catalog changed, relist resources before assuming your cached per-build URIs are still valid

## When to read the coarse workspace

Use `axsg.hotDesign.workspace` or `axsg://runtime/hotdesign/workspace/current` when you need:

- the full element tree
- property entries
- toolbox categories
- full canvas and panel state in one read

Do not use the full workspace snapshot for every tiny repaint when you only need:

- current selection
- current document
- one tracked build URI

That is what the focused resources are for.

## Related docs

- [MCP Servers and Live Tooling](mcp-servers-and-live-tooling/)
- [Runtime MCP Studio Control](runtime-mcp-studio-control/)
- [Preview MCP Host and Live Preview](preview-mcp-host-and-live-preview/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](../reference/runtime-avalonia/)
