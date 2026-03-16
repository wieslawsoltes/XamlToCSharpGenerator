using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.PreviewerHost;

internal sealed class PreviewHostMcpServer : IAsyncDisposable
{
    internal const string StatusResourceUri = "axsg://preview/session/status";
    internal const string EventsResourceUri = "axsg://preview/session/events";
    internal const string CurrentSessionResourceUri = "axsg://preview/session/current";

    private readonly PreviewHostCommandRouter _router;
    private readonly PreviewHostMcpState _state;
    private readonly McpServerCore _server;
    private readonly PreviewHostMcpNotificationBridge _notificationBridge;

    public PreviewHostMcpServer(Stream input, Stream output)
        : this(input, output, sessionFactory: null)
    {
    }

    internal PreviewHostMcpServer(Stream input, Stream output, Func<IPreviewHostSession>? sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        _router = sessionFactory is null ? new PreviewHostCommandRouter() : new PreviewHostCommandRouter(sessionFactory);
        _state = new PreviewHostMcpState();
        _router.EventPublished += OnRouterEvent;

        _server = new McpServerCore(
            new JsonRpcMessageReader(input),
            new JsonRpcMessageWriter(output),
            new McpServerInfo(
                Name: "axsg-preview-mcp",
                Version: typeof(PreviewHostMcpServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                Instructions: "Provides AXSG preview start, hotReload, update, and stop tools. Use axsg.preview.hotReload when you need the in-process live-apply result, subscribe to preview session status/events resources for lifecycle changes, and relist tools/resources when list_changed notifications arrive."),
            GetToolDefinitions,
            GetResourceDefinitions,
            new McpServerCapabilities(
                ToolsListChanged: true,
                ResourcesSubscribe: true,
                ResourcesListChanged: true));

        _notificationBridge = new PreviewHostMcpNotificationBridge(_server, _state);
    }

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        return _server.RunAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _notificationBridge.Dispose();
        _router.EventPublished -= OnRouterEvent;
        await _router.DisposeAsync().ConfigureAwait(false);
        _server.Dispose();
    }

    private IReadOnlyList<McpToolDefinition> GetToolDefinitions()
    {
        var tools = new List<McpToolDefinition>
        {
            new(
                "axsg.preview.start",
                "Start an AXSG preview session.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "hostAssemblyPath", "previewerToolPath", "sourceAssemblyPath", "xamlFileProjectPath", "xamlText" },
                    ("dotNetCommand", McpSchemaBuilder.BuildStringSchema("Optional dotnet command. Defaults to 'dotnet'.")),
                    ("hostAssemblyPath", McpSchemaBuilder.BuildStringSchema("Path to the preview host or app assembly.")),
                    ("previewerToolPath", McpSchemaBuilder.BuildStringSchema("Path to the Avalonia previewer tool assembly.")),
                    ("runtimeConfigPath", McpSchemaBuilder.BuildStringSchema("Optional runtimeconfig.json override for the host assembly.")),
                    ("depsFilePath", McpSchemaBuilder.BuildStringSchema("Optional deps.json override for the host assembly.")),
                    ("sourceAssemblyPath", McpSchemaBuilder.BuildStringSchema("Path to the built source-generated application assembly.")),
                    ("sourceFilePath", McpSchemaBuilder.BuildStringSchema("Optional source file path used for diagnostics.")),
                    ("xamlFileProjectPath", McpSchemaBuilder.BuildStringSchema("Project-relative XAML path sent to the preview host.")),
                    ("xamlText", McpSchemaBuilder.BuildStringSchema("Current XAML text to preview.")),
                    ("previewCompilerMode", McpSchemaBuilder.BuildStringSchema("Preview compiler mode: avalonia, sourceGenerated, or auto.")),
                    ("previewWidth", McpSchemaBuilder.BuildNumberSchema("Optional preview viewport width in logical pixels.")),
                    ("previewHeight", McpSchemaBuilder.BuildNumberSchema("Optional preview viewport height in logical pixels.")),
                    ("previewScale", McpSchemaBuilder.BuildNumberSchema("Optional preview render scale."))),
                HandleStartAsync)
        };

        if (_state.HasActiveSession)
        {
            tools.Add(new McpToolDefinition(
                "axsg.preview.hotReload",
                "Apply an in-process AXSG preview hot reload and wait for the live update result.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "xamlText" },
                    ("xamlText", McpSchemaBuilder.BuildStringSchema("Updated XAML text.")),
                    ("timeoutMs", McpSchemaBuilder.BuildIntegerSchema("Optional timeout in milliseconds for the in-process hot reload result."))),
                HandleHotReloadAsync));

            tools.Add(new McpToolDefinition(
                "axsg.preview.update",
                "Dispatch a live XAML update into the active AXSG preview session without waiting for the in-process apply result.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "xamlText" },
                    ("xamlText", McpSchemaBuilder.BuildStringSchema("Updated XAML text."))),
                HandleUpdateAsync));

            tools.Add(new McpToolDefinition(
                "axsg.preview.stop",
                "Stop the active AXSG preview session.",
                McpSchemaBuilder.BuildObjectSchema(),
                HandleStopAsync));
        }

        return tools;
    }

    private IReadOnlyDictionary<string, McpResourceDefinition> GetResourceDefinitions()
    {
        var resources = new Dictionary<string, McpResourceDefinition>(StringComparer.Ordinal)
        {
            [StatusResourceUri] = new(
                StatusResourceUri,
                "AXSG Preview Status",
                "Current AXSG preview session lifecycle status. Prefer subscribing to this resource instead of polling when connected to the preview MCP host.",
                "application/json",
                _ => ValueTask.FromResult<object?>(_state.GetStatus())),
            [EventsResourceUri] = new(
                EventsResourceUri,
                "AXSG Preview Events",
                "Recent AXSG preview lifecycle and log events.",
                "application/json",
                _ => ValueTask.FromResult<object?>(_state.GetEvents()))
        };

        if (_state.HasActiveSession)
        {
            resources[CurrentSessionResourceUri] = new McpResourceDefinition(
                CurrentSessionResourceUri,
                "AXSG Active Preview Session",
                "Details for the currently active AXSG preview session.",
                "application/json",
                _ => ValueTask.FromResult<object?>(_state.GetCurrentSession()));
        }

        return resources;
    }

    private async ValueTask<object?> HandleStartAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        JsonElement payload = SerializeArguments(arguments);
        AxsgPreviewHostStartRequest request = AxsgPreviewHostProtocol.ParseStartRequest(payload);
        _state.MarkStartRequested(request);

        AxsgPreviewHostResponseEnvelope response = await _router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.StartCommand,
                CreateRequestId(),
                payload),
            cancellationToken).ConfigureAwait(false);

        if (!response.Ok)
        {
            string error = string.IsNullOrWhiteSpace(response.Error) ? "Preview start failed." : response.Error;
            _state.MarkStartFailed(error);
            throw new McpToolException(new { message = error });
        }

        AxsgPreviewHostStartResponse result = DeserializePayload<AxsgPreviewHostStartResponse>(response.Payload, "start response");
        _state.MarkStarted(result);
        await NotifyPreviewLifecycleAsync(cancellationToken, includeStatus: true, includeEvents: true).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<object?> HandleUpdateAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        JsonElement payload = SerializeArguments(arguments);
        AxsgPreviewHostProtocol.ParseUpdateRequest(payload);
        _state.MarkUpdateRequested();

        AxsgPreviewHostResponseEnvelope response = await _router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.UpdateCommand,
                CreateRequestId(),
                payload),
            cancellationToken).ConfigureAwait(false);

        if (!response.Ok)
        {
            string error = string.IsNullOrWhiteSpace(response.Error) ? "Preview update failed." : response.Error;
            _state.MarkUpdateDispatchFailed(error);
            await NotifyPreviewLifecycleAsync(cancellationToken, includeStatus: true, includeEvents: true).ConfigureAwait(false);
            throw new McpToolException(new { message = error });
        }

        return new
        {
            accepted = true
        };
    }

    private async ValueTask<object?> HandleHotReloadAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        JsonElement payload = SerializeArguments(arguments);
        AxsgPreviewHostProtocol.ParseHotReloadRequest(payload);
        _state.MarkUpdateRequested();

        AxsgPreviewHostResponseEnvelope response = await _router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.HotReloadCommand,
                CreateRequestId(),
                payload),
            cancellationToken).ConfigureAwait(false);

        if (!response.Ok)
        {
            string error = string.IsNullOrWhiteSpace(response.Error) ? "Preview hot reload failed." : response.Error;
            _state.MarkUpdateDispatchFailed(error);
            throw new McpToolException(new { message = error });
        }

        return DeserializePayload<AxsgPreviewHostHotReloadResponse>(response.Payload, "hot reload response");
    }

    private async ValueTask<object?> HandleStopAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        _ = arguments;

        AxsgPreviewHostResponseEnvelope response = await _router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.StopCommand,
                CreateRequestId(),
                default),
            cancellationToken).ConfigureAwait(false);

        if (!response.Ok)
        {
            string error = string.IsNullOrWhiteSpace(response.Error) ? "Preview stop failed." : response.Error;
            throw new McpToolException(new { message = error });
        }

        _state.MarkStopped();
        await NotifyPreviewLifecycleAsync(cancellationToken, includeStatus: true, includeEvents: true).ConfigureAwait(false);
        return new
        {
            stopped = true
        };
    }

    private void OnRouterEvent(AxsgPreviewHostEventEnvelope envelope)
    {
        switch (envelope.Event)
        {
            case AxsgPreviewHostProtocol.LogEvent:
                AxsgPreviewHostLogEventPayload logPayload = DeserializePayload<AxsgPreviewHostLogEventPayload>(envelope.Payload, "log event");
                _state.AppendLog(logPayload.Message);
                _ = NotifyPreviewLifecycleAsync(CancellationToken.None, includeStatus: false, includeEvents: true);
                break;

            case AxsgPreviewHostProtocol.UpdateResultEvent:
                AxsgPreviewHostUpdateResultEventPayload updateResultPayload = DeserializePayload<AxsgPreviewHostUpdateResultEventPayload>(envelope.Payload, "update result event");
                _state.MarkUpdateCompleted(updateResultPayload);
                _ = NotifyPreviewLifecycleAsync(CancellationToken.None, includeStatus: true, includeEvents: true);
                break;

            case AxsgPreviewHostProtocol.HostExitedEvent:
                AxsgPreviewHostHostExitedEventPayload hostExitedPayload = DeserializePayload<AxsgPreviewHostHostExitedEventPayload>(envelope.Payload, "host exited event");
                _state.MarkHostExited(hostExitedPayload.ExitCode);
                _ = NotifyPreviewLifecycleAsync(CancellationToken.None, includeStatus: true, includeEvents: true);
                break;
        }
    }

    private static string CreateRequestId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static JsonElement SerializeArguments(JsonObject? arguments)
    {
        JsonObject payload = arguments ?? new JsonObject();
        return JsonSerializer.SerializeToDocument(payload, JsonRpcSerializer.DefaultOptions).RootElement.Clone();
    }

    private static T DeserializePayload<T>(JsonNode? payload, string payloadName)
        where T : class
    {
        T? result = payload?.Deserialize<T>(JsonRpcSerializer.DefaultOptions);
        return result ?? throw new InvalidOperationException("Invalid " + payloadName + ".");
    }

    private async Task NotifyPreviewLifecycleAsync(CancellationToken cancellationToken, bool includeStatus, bool includeEvents)
    {
        if (includeStatus)
        {
            await _server.NotifyResourceUpdatedAsync(StatusResourceUri, cancellationToken).ConfigureAwait(false);
        }

        if (includeEvents)
        {
            await _server.NotifyResourceUpdatedAsync(EventsResourceUri, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class PreviewHostMcpNotificationBridge : IDisposable
{
    private readonly McpServerCore _server;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private readonly HashSet<string> _pendingResourceUris = new(StringComparer.Ordinal);
    private Task _notificationQueue = Task.CompletedTask;
    private bool _pendingToolsListChanged;
    private bool _pendingResourcesListChanged;
    private bool _disposed;
    private bool _flushScheduled;

    public PreviewHostMcpNotificationBridge(McpServerCore server, PreviewHostMcpState state)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        ArgumentNullException.ThrowIfNull(state);

        _shutdownToken = _shutdown.Token;
        state.ResourceUpdated += OnResourceUpdated;
        state.ToolsListChanged += OnToolsListChanged;
        state.ResourcesListChanged += OnResourcesListChanged;
        State = state;
    }

    private PreviewHostMcpState State { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        State.ResourceUpdated -= OnResourceUpdated;
        State.ToolsListChanged -= OnToolsListChanged;
        State.ResourcesListChanged -= OnResourcesListChanged;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private void OnResourceUpdated(string resourceUri)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingResourceUris.Add(resourceUri);
            ScheduleFlushLocked();
        }
    }

    private void OnToolsListChanged()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingToolsListChanged = true;
            ScheduleFlushLocked();
        }
    }

    private void OnResourcesListChanged()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingResourcesListChanged = true;
            ScheduleFlushLocked();
        }
    }

    private void ScheduleFlushLocked()
    {
        if (_flushScheduled)
        {
            return;
        }

        _flushScheduled = true;
        _notificationQueue = _notificationQueue.ContinueWith(
            static (_, state) => ((PreviewHostMcpNotificationBridge)state!).FlushAsync(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
    }

    private async Task FlushAsync()
    {
        while (true)
        {
            string[] resourceUris;
            bool toolsListChanged;
            bool resourcesListChanged;
            lock (_gate)
            {
                if (_disposed)
                {
                    _flushScheduled = false;
                    return;
                }

                if (_pendingResourceUris.Count == 0 && !_pendingToolsListChanged && !_pendingResourcesListChanged)
                {
                    _flushScheduled = false;
                    return;
                }

                resourceUris = new string[_pendingResourceUris.Count];
                _pendingResourceUris.CopyTo(resourceUris);
                _pendingResourceUris.Clear();
                toolsListChanged = _pendingToolsListChanged;
                resourcesListChanged = _pendingResourcesListChanged;
                _pendingToolsListChanged = false;
                _pendingResourcesListChanged = false;
            }

            try
            {
                if (toolsListChanged)
                {
                    await _server.NotifyToolsListChangedAsync(_shutdownToken).ConfigureAwait(false);
                }

                if (resourcesListChanged)
                {
                    await _server.NotifyResourcesListChangedAsync(_shutdownToken).ConfigureAwait(false);
                }

                for (int index = 0; index < resourceUris.Length; index++)
                {
                    await _server.NotifyResourceUpdatedAsync(resourceUris[index], _shutdownToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Notification delivery is best effort.
            }
        }
    }
}
