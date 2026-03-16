---
title: "Runtime MCP Studio Control"
---

# Runtime MCP Studio Control

Use this guide when you want to drive AXSG studio state from MCP against a live Avalonia process.

Studio MCP control lives on the runtime host because studio is part of the running application state.

## Use this guide when

- you want to enable or configure studio remotely
- you need to start or stop studio sessions
- you want to query studio scopes and then apply a studio update
- you want one MCP result that includes the update result, studio status, and refreshed hot design workspace

## Studio tools

- `axsg.studio.enable`
- `axsg.studio.disable`
- `axsg.studio.configure`
- `axsg.studio.startSession`
- `axsg.studio.stopSession`
- `axsg.studio.applyUpdate`
- `axsg.studio.scopes`
- `axsg.studio.status`

## Studio resources

- `axsg://runtime/studio/status`
- `axsg://runtime/studio/scopes`
- `axsg://runtime/studio/events`

In practice, studio clients often also subscribe to:

- `axsg://runtime/hotdesign/workspace/current`

because the studio update result is usually interpreted together with the active hot design workspace.

## Recommended client model

Use this rule:

- subscribe to `studio/status` and `studio/events`
- read `studio/scopes` when scope selection UI needs a refresh
- subscribe to `hotdesign/workspace/current` if the studio UX also shows the active design surface

## 1. Enable or configure studio

### Minimal enable

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "axsg.studio.enable",
    "arguments": {
      "showOverlayIndicator": false,
      "enableExternalWindow": false
    }
  }
}
```

### Configure an existing session

```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "method": "tools/call",
  "params": {
    "name": "axsg.studio.configure",
    "arguments": {
      "canvasLayoutMode": "Stacked",
      "enableRemoteDesign": true,
      "remoteHost": "127.0.0.1",
      "remotePort": 45832,
      "waitMode": "HotReload",
      "fallbackPolicy": "RuntimeApply"
    }
  }
}
```

Both tools return the full studio status snapshot, including:

- `sessionId`
- `currentState`
- `options`
- `remote`
- `scopes`
- recent `operations`

## 2. Start a studio session

```json
{
  "jsonrpc": "2.0",
  "id": 12,
  "method": "tools/call",
  "params": {
    "name": "axsg.studio.startSession",
    "arguments": {}
  }
}
```

The returned status payload includes the new `sessionId`.

## 3. Read studio scopes

```json
{
  "jsonrpc": "2.0",
  "id": 13,
  "method": "resources/read",
  "params": {
    "uri": "axsg://runtime/studio/scopes"
  }
}
```

Each scope entry includes:

- `scopeKind`
- `id`
- `displayName`
- `targetTypeName`
- `buildUri`

Use that payload when the client needs to present scope selection without pulling the full studio status object again.

## 4. Apply a studio update

`axsg.studio.applyUpdate` is the studio mutation entry point.

Example:

```json
{
  "jsonrpc": "2.0",
  "id": 14,
  "method": "tools/call",
  "params": {
    "name": "axsg.studio.applyUpdate",
    "arguments": {
      "requestId": "studio-001",
      "correlationId": 42,
      "buildUri": "avares://MyApp/Views/MainView.axaml",
      "xamlText": "<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock Text=\"Studio\"/></UserControl>",
      "waitMode": "HotReload",
      "fallbackPolicy": "RuntimeApply",
      "persistChangesToSource": true,
      "timeoutMs": 3000
    }
  }
}
```

Result shape:

```json
{
  "applyResult": {
    "succeeded": true,
    "state": "Completed",
    "requestId": "studio-001",
    "correlationId": 42,
    "buildUri": "avares://MyApp/Views/MainView.axaml",
    "sourcePersisted": true,
    "localUpdateObserved": true,
    "runtimeFallbackApplied": false
  },
  "status": {
    "isEnabled": true,
    "currentState": "Ready"
  },
  "workspace": {
    "activeBuildUri": "avares://MyApp/Views/MainView.axaml"
  }
}
```

That combined response is deliberate:

- `applyResult` tells you what happened
- `status` tells you what studio thinks the session state is now
- `workspace` gives the refreshed hot design projection for the updated target

## Wait mode and fallback policy

The studio apply path supports:

- `waitMode`
- `fallbackPolicy`
- `persistChangesToSource`
- `timeoutMs`

Use them when your client needs explicit control over whether it waits for hot reload completion or allows runtime fallback behavior.

Typical choices:

- optimistic local designer: `waitMode = None`
- synchronized live-app tooling: `waitMode = HotReload`
- resilient editing tool: `fallbackPolicy = RuntimeApply`

## Subscribe-then-apply flow

Recommended pattern:

1. subscribe to `axsg://runtime/studio/status`
2. subscribe to `axsg://runtime/studio/events`
3. optionally subscribe to `axsg://runtime/hotdesign/workspace/current`
4. call `axsg.studio.applyUpdate`
5. consume the direct tool result
6. keep the subscribed resources alive for later status and operation changes

This keeps the client responsive:

- direct response for the mutation that just happened
- subscriptions for everything that happens afterward

## Stop the session

```json
{
  "jsonrpc": "2.0",
  "id": 15,
  "method": "tools/call",
  "params": {
    "name": "axsg.studio.stopSession",
    "arguments": {}
  }
}
```

This returns the updated status snapshot after the current session has been cleared.

## Relationship to hot design MCP control

Studio and hot design overlap, but they are not the same surface:

- hot design MCP is the direct design-workspace and edit-control surface
- studio MCP is the session-oriented orchestration surface

Use hot design tools when you want direct element/document/canvas edits.

Use studio tools when you want:

- studio session lifecycle
- studio-scoped update orchestration
- studio option and remote design configuration

## Related docs

- [Runtime MCP Hot Design Control](runtime-mcp-hot-design-control/)
- [MCP Servers and Live Tooling](mcp-servers-and-live-tooling/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](../reference/runtime-avalonia/)
