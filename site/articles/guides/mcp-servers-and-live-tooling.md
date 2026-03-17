---
title: "MCP Servers and Live Tooling"
---

# MCP Servers and Live Tooling

AXSG now exposes a unified MCP-oriented remote API across workspace tooling, runtime hot reload and hot design services, and preview orchestration. This guide explains which host to run, how it relates to `dotnet watch`, and how it fits with the VS Code extension.

## Host matrix

| Host | How you run it | Best for | Live runtime state | Subscribe support |
| --- | --- | --- | --- | --- |
| Workspace MCP host | `axsg-mcp --workspace /path/to/repo` | workspace queries, preview project resolution, agent/tool integration outside the app process | no | no |
| Runtime MCP host | embed `XamlSourceGenRuntimeMcpServer` into the running Avalonia app | hot reload, hot design, studio, watched app inspection | yes | yes |
| Preview MCP host | `dotnet run --project src/XamlToCSharpGenerator.PreviewerHost -- --mcp` | preview lifecycle control for custom clients and test harnesses | preview-session state only | yes |
| VS Code extension | install the VSIX and use normal AXSG commands | packaged editing and preview experience | extension-owned | n/a |

The key distinction is process ownership:

- the workspace host sees files and projects
- the runtime host sees the live application
- the preview host sees one preview session

## 1. Run the workspace MCP host

Install the tool:

```bash
dotnet tool install --global XamlToCSharpGenerator.McpServer.Tool --version x.y.z
```

Run it against a workspace:

```bash
axsg-mcp --workspace /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator
```

For repo development you can also run it directly from source:

```bash
dotnet run --project src/XamlToCSharpGenerator.McpServer -- --workspace /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator
```

Use this host when you need:

- `axsg.preview.projectContext`
- workspace-shaped hot reload or hot design queries
- an MCP surface that can be launched independently from the app

Do not use this host when you need live app state from a running `dotnet watch` session. It is query-oriented and does not attach to another process automatically.

## 2. Run the runtime MCP host inside a watched app

The runtime MCP host is not a separate executable. You embed it into the Avalonia application that is already running AXSG hot reload or hot design.

### Step 1. Enable AXSG runtime services

Typical app bootstrap:

```csharp
using Avalonia;
using XamlToCSharpGenerator.Runtime;

internal static class Program
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            .UseAvaloniaSourceGeneratedRuntimeXamlCompilation(enable: true)
            .UseAvaloniaSourceGeneratedXamlHotDesign(enable: true, configure: options =>
            {
                options.PersistChangesToSource = true;
                options.WaitForHotReload = false;
            })
            .UseAvaloniaSourceGeneratedStudioFromEnvironment()
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);
    }
}
```

That setup gives the runtime host something meaningful to expose:

- hot reload status
- hot design status and registered documents
- hot design workspace snapshots
- studio state and event streams

### Step 2. Provide a transport

`XamlSourceGenRuntimeMcpServer` is stream-based:

```csharp
new XamlSourceGenRuntimeMcpServer(Stream input, Stream output)
```

AXSG intentionally does not hard-code a transport policy for the live app host. That choice belongs to the embedding application.

Recommended transports:

- loopback TCP for desktop tooling and local AI agents
- named pipes or Unix domain sockets when you want tighter local-only attachment
- stdio only when the app is launched specifically as an MCP child process

For a normal `dotnet watch` desktop app, loopback TCP or a named pipe is the practical choice.

### Step 3. Embed a simple loopback host

The example below uses a host-defined environment variable, `AXSG_RUNTIME_MCP_PORT`. That variable is not built into AXSG; it is just one simple convention for your app.

```csharp
using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using XamlToCSharpGenerator.Runtime;

internal sealed class AxsgRuntimeMcpTcpHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public AxsgRuntimeMcpTcpHost(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

                _ = Task.Run(async () =>
                {
                    await using var stream = client.GetStream();
                    using var server = new XamlSourceGenRuntimeMcpServer(stream, stream);
                    await server.RunAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        if (_acceptLoop is not null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }

        _cts.Dispose();
    }
}

internal static class Program
{
    private static AxsgRuntimeMcpTcpHost? _runtimeMcpHost;

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseAvaloniaSourceGeneratedXaml()
            .UseAvaloniaSourceGeneratedXamlHotDesign(enable: true)
            .UseAvaloniaSourceGeneratedStudioFromEnvironment()
            .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);

        if (int.TryParse(Environment.GetEnvironmentVariable("AXSG_RUNTIME_MCP_PORT"), out int port) && port > 0)
        {
            builder = builder.AfterSetup(_ =>
            {
                _runtimeMcpHost = new AxsgRuntimeMcpTcpHost(port);
                _runtimeMcpHost.Start();

                if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
                {
                    lifetime.Exit += async (_, _) =>
                    {
                        if (_runtimeMcpHost is not null)
                        {
                            await _runtimeMcpHost.DisposeAsync().ConfigureAwait(false);
                        }
                    };
                }
            });
        }

        return builder;
    }
}
```

### Step 4. Run it with `dotnet watch`

```bash
AXSG_RUNTIME_MCP_PORT=43110 \
dotnet watch run --project samples/SourceGenCrudSample/SourceGenCrudSample.csproj
```

Once the app is running, connect your MCP client to the loopback server you exposed.

The runtime MCP host is the one you use for:

- hot reload status subscriptions
- hot design document and workspace inspection
- hot design control and edit application
- studio status, scopes, and session/event streams

## 3. Hot reload, hot design, and studio resources

The runtime host exposes the live runtime-facing resources:

- `axsg://runtime/hotreload/status`
- `axsg://runtime/hotreload/events`
- `axsg://runtime/hotdesign/status`
- `axsg://runtime/hotdesign/documents`
- `axsg://runtime/hotdesign/workspace/current`
- `axsg://runtime/hotdesign/document/selected`
- `axsg://runtime/hotdesign/element/selected`
- `axsg://runtime/hotdesign/events`
- `axsg://runtime/studio/status`
- `axsg://runtime/studio/scopes`
- `axsg://runtime/studio/events`

The runtime host also publishes one dynamic per-build workspace resource per registered hot design document:

```text
axsg://runtime/hotdesign/workspace/by-build-uri/<escaped-build-uri>
```

The matching tools are:

- `axsg.hotReload.status`
- `axsg.hotReload.enable`
- `axsg.hotReload.disable`
- `axsg.hotReload.toggle`
- `axsg.hotReload.trackedDocuments`
- `axsg.hotReload.remoteTransportStatus`
- `axsg.hotReload.lastOperation`
- `axsg.hotDesign.status`
- `axsg.hotDesign.documents`
- `axsg.hotDesign.workspace`
- `axsg.hotDesign.selectDocument`
- `axsg.hotDesign.selectElement`
- `axsg.hotDesign.applyDocumentText`
- `axsg.hotDesign.applyPropertyUpdate`
- `axsg.hotDesign.insertElement`
- `axsg.hotDesign.removeElement`
- `axsg.hotDesign.undo`
- `axsg.hotDesign.redo`
- `axsg.hotDesign.setWorkspaceMode`
- `axsg.hotDesign.setPropertyFilterMode`
- `axsg.hotDesign.setHitTestMode`
- `axsg.hotDesign.togglePanel`
- `axsg.hotDesign.setPanelVisibility`
- `axsg.hotDesign.setCanvasZoom`
- `axsg.hotDesign.setCanvasFormFactor`
- `axsg.hotDesign.setCanvasTheme`
- `axsg.studio.enable`
- `axsg.studio.disable`
- `axsg.studio.configure`
- `axsg.studio.startSession`
- `axsg.studio.stopSession`
- `axsg.studio.applyUpdate`
- `axsg.studio.scopes`
- `axsg.studio.status`

Recommended client behavior:

- use `tools/call` for mutations and one-shot reads
- use `resources/subscribe` for focused status and event resources in a live app
- relist resources after `notifications/resources/list_changed` if you care about per-build workspace resources

For the detailed control surfaces:

- [Runtime MCP Hot Design Control](runtime-mcp-hot-design-control/)
- [Runtime MCP Studio Control](runtime-mcp-studio-control/)
- [Workspace MCP Language Tools](workspace-mcp-language-tools/)

## 4. Run the preview host in MCP mode

The preview host also supports MCP directly:

```bash
dotnet run --project src/XamlToCSharpGenerator.PreviewerHost -- --mcp
```

This host exposes preview session tools:

- `axsg.preview.start`
- `axsg.preview.hotReload`
- `axsg.preview.update`
- `axsg.preview.stop`

and preview lifecycle resources:

- `axsg://preview/session/status`
- `axsg://preview/session/events`
- `axsg://preview/session/current`

The preview host is the right MCP surface when you are building:

- a custom preview client
- automated preview smoke tests
- AI tooling that needs explicit preview start, hot reload, update, stop, and lifecycle control

It is not the same thing as the runtime MCP host. The runtime host mirrors the running application. The preview host owns a dedicated preview session.

Use this rule for preview mutations:

- `axsg.preview.hotReload`: apply and wait for the in-process live-preview result
- `axsg.preview.update`: dispatch-only update when your client already handles completion asynchronously

For the full preview-host workflow and tool semantics, see [Preview MCP Host and Live Preview](preview-mcp-host-and-live-preview/).

## 5. How this fits with the VS Code extension

For normal VS Code usage, you do not need to start any MCP host manually.

The extension already manages:

- the bundled language server
- preview project resolution
- preview helper startup
- live preview update routing

The current VS Code preview UI still uses the extension’s bundled helper transport for the product workflow. The preview host’s MCP mode exists for custom clients, testing, and future remote-integration work; it is not the primary extension transport today.

The workspace MCP host is still useful alongside VS Code when you want:

- external AI or tool access to workspace queries
- an MCP client that resolves preview project context independently from the editor

## 6. Poll vs subscribe guidance

Use this rule:

- workspace MCP host: poll
- runtime MCP host: subscribe
- preview MCP host: subscribe

More concretely:

- `axsg-mcp` is stateless enough that `tools/list`, `tools/call`, and `resources/read` are the expected workflow
- `XamlSourceGenRuntimeMcpServer` should be treated as a live stream of status, focused snapshot, and event resources
- `PreviewHostMcpServer` supports dynamic tools and resources, so relist after `notifications/tools/list_changed` or `notifications/resources/list_changed`

## 7. Troubleshooting

### I started `axsg-mcp`, but hot reload state is empty

That is expected unless you embedded the runtime MCP host into the running app. The standalone tool does not attach to another process automatically.

### I want `dotnet watch` plus MCP without editing the app

That is not the current architecture. Today, live runtime MCP is in-process by design. Add a small transport host to the app and let `dotnet watch` restart it normally.

### I only need preview orchestration

Use the preview host MCP mode instead of the runtime host.

### I need the preview apply result, not just request dispatch

Use `axsg.preview.hotReload` instead of `axsg.preview.update`.

### I only need project resolution for preview

Use `axsg-mcp --workspace ...` and call `axsg.preview.projectContext`.

## Related docs

- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Workspace MCP Language Tools](workspace-mcp-language-tools/)
- [Runtime MCP Hot Design Control](runtime-mcp-hot-design-control/)
- [Runtime MCP Studio Control](runtime-mcp-studio-control/)
- [Preview MCP Host and Live Preview](preview-mcp-host-and-live-preview/)
- [Hot Reload and Hot Design](hot-reload-and-hot-design/)
- [VS Code and Language Service](vscode-language-service/)
- [Package: XamlToCSharpGenerator.McpServer.Tool](../reference/mcp-server-tool/)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](../reference/runtime-avalonia/)
