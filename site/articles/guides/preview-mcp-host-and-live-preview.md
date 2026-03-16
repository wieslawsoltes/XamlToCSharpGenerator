---
title: "Preview MCP Host and Live Preview"
---

# Preview MCP Host and Live Preview

Use this guide when you need to drive AXSG preview outside the packaged VS Code workflow.

Typical cases:

- a custom MCP client that needs to start and control preview sessions
- automated preview smoke tests
- AI tooling that wants explicit preview start, hot reload, update, and stop operations
- debugging preview lifecycle state independently from the VS Code extension

This guide is specifically about the preview host:

- `dotnet run --project src/XamlToCSharpGenerator.PreviewerHost -- --mcp`

It is not the same thing as:

- `axsg-mcp`, which is the standalone workspace MCP host
- `XamlSourceGenRuntimeMcpServer`, which is the in-process runtime MCP host inside a running Avalonia app

## What the preview MCP host owns

The preview host owns one preview session at a time.

It exposes:

- preview session startup
- in-process live preview hot reload
- low-level live update dispatch
- preview stop
- preview session status and lifecycle events

The host uses the same shared AXSG preview protocol contracts as the lightweight helper transport and the VS Code preview bridge. The MCP mode adds MCP tools/resources on top of that shared session layer; it does not introduce a separate preview engine.

## Start the host

Run it from source:

```bash
dotnet run --project src/XamlToCSharpGenerator.PreviewerHost -- --mcp
```

The host runs over JSON-RPC stdio, so a client typically launches it as a child process and then sends the MCP handshake:

1. `initialize`
2. `notifications/initialized`
3. `tools/list` and `resources/list`

Once initialized, the preview host supports:

- `notifications/tools/list_changed`
- `notifications/resources/list_changed`
- `resources/subscribe`
- `notifications/resources/updated`

That matters because the preview tool/resource catalog changes when a session becomes active or stops.

## Session model

The preview MCP host starts in an idle state.

Before a session exists, the useful tool is:

- `axsg.preview.start`

After a session starts, the host dynamically adds:

- `axsg.preview.hotReload`
- `axsg.preview.update`
- `axsg.preview.stop`

It also adds the dynamic resource:

- `axsg://preview/session/current`

When the session stops or the preview host exits, those dynamic tools/resources disappear again, and the host publishes the appropriate list-changed notifications.

## Tools

### `axsg.preview.start`

Starts the preview session and returns:

- preview URL
- transport port
- preview HTML port
- session ID

Required arguments:

- `hostAssemblyPath`
- `previewerToolPath`
- `sourceAssemblyPath`
- `xamlFileProjectPath`
- `xamlText`

Common optional arguments:

- `dotNetCommand`
- `runtimeConfigPath`
- `depsFilePath`
- `sourceFilePath`
- `previewCompilerMode`
- `previewWidth`
- `previewHeight`
- `previewScale`

`previewCompilerMode` accepts:

- `sourceGenerated`
- `avalonia`
- `auto`

For most custom AXSG preview clients, `sourceGenerated` is the right default when you want AXSG live-preview semantics.

### `axsg.preview.hotReload`

This is the preferred mutation tool when you need a real in-process apply result.

It:

- sends updated XAML into the active preview session
- waits for the designer host to complete the apply
- returns the success or failure result directly

Arguments:

- `xamlText`
- optional `timeoutMs`

Use this tool when your client wants “apply and wait”.

This is the MCP operation that closes the gap between preview lifecycle control and real source-generated in-process live preview updates.

### `axsg.preview.update`

This is the lower-level dispatch tool.

It:

- sends updated XAML into the active preview session
- returns immediately once the request is accepted for dispatch
- does not wait for the in-process apply result

Use this when:

- you are already subscribed to preview lifecycle resources
- you want a fire-and-forget flow
- you are mirroring the old helper behavior and handling completion asynchronously

### `axsg.preview.stop`

Stops the active preview session and removes the session-specific tool/resource catalog entries.

## Resources

### `axsg://preview/session/status`

The current session lifecycle snapshot.

This resource includes fields such as:

- `phase`
- `isSessionActive`
- `previewUrl`
- `sessionId`
- `transportPort`
- `previewPort`
- `previewCompilerMode`
- `sourceAssemblyPath`
- `xamlFileProjectPath`
- `lastUpdateSucceeded`
- `lastError`
- `lastException`
- `updatedAtUtc`

### `axsg://preview/session/events`

Recent bounded preview lifecycle and log events.

This is the resource to subscribe to when you want:

- start and stop visibility
- hot reload success/failure visibility
- preview-host log messages

### `axsg://preview/session/current`

The dynamic “current session” snapshot.

This resource only exists while a session is active.

## Recommended client behavior

Use this split:

- use `axsg.preview.start` once per session
- use `axsg.preview.hotReload` when you need a synchronous live-apply result
- use `axsg.preview.update` only when you intentionally want fire-and-forget dispatch
- subscribe to `axsg://preview/session/status`
- subscribe to `axsg://preview/session/events`
- relist tools/resources after `notifications/tools/list_changed` or `notifications/resources/list_changed`

For a custom editor or agent, `hotReload` is usually the correct update operation. `update` is mostly the compatibility/low-level transport operation.

## Example flow

High-level flow:

1. start the preview host in MCP mode
2. initialize the MCP session
3. call `axsg.preview.start`
4. subscribe to status and event resources
5. push edits through `axsg.preview.hotReload`
6. read the direct result and keep listening for lifecycle changes
7. call `axsg.preview.stop` when done

Example `tools/call` for `axsg.preview.hotReload`:

```json
{
  "jsonrpc": "2.0",
  "id": 31,
  "method": "tools/call",
  "params": {
    "name": "axsg.preview.hotReload",
    "arguments": {
      "xamlText": "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
      "timeoutMs": 2500
    }
  }
}
```

Typical successful response shape:

```json
{
  "jsonrpc": "2.0",
  "id": 31,
  "result": {
    "structuredContent": {
      "succeeded": true,
      "error": null,
      "exception": null,
      "completedAtUtc": "2026-03-16T12:34:56.789+00:00"
    }
  }
}
```

## Relationship to the VS Code extension

The normal VS Code extension preview path does not require you to launch the preview MCP host manually.

The packaged extension still owns:

- preview webview creation
- bundled helper startup
- editor integration
- VS Code-specific preview UX

The preview MCP host exists for:

- custom clients
- tests
- future remote integrations
- explicit preview orchestration outside the product UX

## Troubleshooting

### I need the preview apply result, not just dispatch

Use `axsg.preview.hotReload`, not `axsg.preview.update`.

### My client caches the tool list and cannot see `hotReload` or `stop`

Relist tools after `notifications/tools/list_changed`. Those tools are only present while a preview session is active.

### My client reads `axsg://preview/session/current` before starting preview

That resource is dynamic and only exists while a session is active.

### I need workspace project resolution before starting preview

Use the workspace MCP host first:

```bash
axsg-mcp --workspace /path/to/workspace
```

and call `axsg.preview.projectContext`.

## Related docs

- [MCP Servers and Live Tooling](mcp-servers-and-live-tooling/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Artifact: XamlToCSharpGenerator.PreviewerHost](../reference/preview-host/)
- [Package: XamlToCSharpGenerator.RemoteProtocol](../reference/remote-protocol/)
- [VS Code and Language Service](vscode-language-service/)
