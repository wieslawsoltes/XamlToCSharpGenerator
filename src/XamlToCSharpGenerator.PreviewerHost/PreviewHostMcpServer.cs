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
    internal const string DesignWorkspaceCurrentResourceUri = "axsg://preview/design/workspace/current";
    internal const string DesignDocumentsSelectedResourceUri = "axsg://preview/design/documents/selected";
    internal const string DesignElementSelectedResourceUri = "axsg://preview/design/element/selected";
    internal const string DesignLogicalTreeResourceUri = "axsg://preview/design/tree/logical";
    internal const string DesignVisualTreeResourceUri = "axsg://preview/design/tree/visual";
    internal const string DesignOverlayCurrentResourceUri = "axsg://preview/design/overlay/current";
    internal const string DesignWorkspaceByBuildUriResourcePrefix = "axsg://preview/design/workspace/by-build-uri/";

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
                Instructions: "Provides AXSG preview lifecycle tools plus AXSG preview design tools/resources for the active session. Use axsg.preview.hotReload when you need the in-process live-apply result, axsg.preview.design.* for selection/tree/property mutations, subscribe to preview session status/events resources for lifecycle changes, and relist tools/resources when list_changed notifications arrive."),
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

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.selectDocument",
                "Select the active design document for the current preview session.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "buildUri" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Build URI for the target XAML document."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("selectDocument", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.selectElement",
                "Select an element in the active preview design workspace.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "elementId" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("elementId", McpSchemaBuilder.BuildStringSchema("Source element identifier to select."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("selectElement", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.selectAtPoint",
                "Run preview hit testing at a logical preview point and optionally update selection.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "x", "y" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("x", McpSchemaBuilder.BuildNumberSchema("Logical preview X coordinate.")),
                    ("y", McpSchemaBuilder.BuildNumberSchema("Logical preview Y coordinate.")),
                    ("updateSelection", McpSchemaBuilder.BuildBooleanSchema("When true, updates the selected element; otherwise only updates hover overlay.")),
                    ("hitTestMode", McpSchemaBuilder.BuildStringSchema("Optional hit-test mode override: Logical or Visual."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("selectAtPoint", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.setWorkspaceMode",
                "Set the active AXSG preview design workspace mode.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "mode" },
                    ("mode", McpSchemaBuilder.BuildStringSchema("Workspace mode: Interactive, Design, or Agent."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("setWorkspaceMode", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.setHitTestMode",
                "Set the preview design hit-test mode.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "mode" },
                    ("mode", McpSchemaBuilder.BuildStringSchema("Hit-test mode: Logical or Visual."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("setHitTestMode", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.setPropertyFilterMode",
                "Set the preview design property-filter mode.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "mode" },
                    ("mode", McpSchemaBuilder.BuildStringSchema("Property filter mode: Smart or All."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("setPropertyFilterMode", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.applyPropertyUpdate",
                "Apply a property edit through the AXSG design workspace and return the minimal-diff update result.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "propertyName" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("elementId", McpSchemaBuilder.BuildStringSchema("Optional target element identifier.")),
                    ("propertyName", McpSchemaBuilder.BuildStringSchema("Property name to set or reset.")),
                    ("propertyValue", McpSchemaBuilder.BuildStringSchema("Property value to apply.")),
                    ("removeProperty", McpSchemaBuilder.BuildBooleanSchema("When true, removes or resets the property."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("applyPropertyUpdate", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.insertElement",
                "Insert a XAML element through the AXSG design workspace and return the minimal-diff update result.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "elementName" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("parentElementId", McpSchemaBuilder.BuildStringSchema("Optional parent element identifier.")),
                    ("elementName", McpSchemaBuilder.BuildStringSchema("Element type name to insert.")),
                    ("xamlFragment", McpSchemaBuilder.BuildStringSchema("Optional explicit XAML fragment to insert."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("insertElement", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.removeElement",
                "Remove an element through the AXSG design workspace and return the minimal-diff update result.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "elementId" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("elementId", McpSchemaBuilder.BuildStringSchema("Element identifier to remove."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("removeElement", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.undo",
                "Undo the last AXSG preview design mutation for the selected build URI.",
                McpSchemaBuilder.BuildObjectSchema(
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("undo", arguments, cancellationToken)));

            tools.Add(new McpToolDefinition(
                "axsg.preview.design.redo",
                "Redo the last AXSG preview design mutation for the selected build URI.",
                McpSchemaBuilder.BuildObjectSchema(
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override."))),
                (arguments, cancellationToken) => HandleDesignToolAsync("redo", arguments, cancellationToken)));
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

            resources[DesignWorkspaceCurrentResourceUri] = new McpResourceDefinition(
                DesignWorkspaceCurrentResourceUri,
                "AXSG Preview Design Workspace",
                "Current AXSG preview design workspace snapshot for the active session.",
                "application/json",
                cancellationToken => ReadDesignResourceAsync("workspace.current", null, cancellationToken));
            resources[DesignDocumentsSelectedResourceUri] = new McpResourceDefinition(
                DesignDocumentsSelectedResourceUri,
                "AXSG Preview Selected Document",
                "Currently selected AXSG preview design document.",
                "application/json",
                cancellationToken => ReadDesignResourceAsync("documents.selected", null, cancellationToken));
            resources[DesignElementSelectedResourceUri] = new McpResourceDefinition(
                DesignElementSelectedResourceUri,
                "AXSG Preview Selected Element",
                "Currently selected AXSG preview design element.",
                "application/json",
                cancellationToken => ReadDesignResourceAsync("element.selected", null, cancellationToken));
            resources[DesignLogicalTreeResourceUri] = new McpResourceDefinition(
                DesignLogicalTreeResourceUri,
                "AXSG Preview Logical Tree",
                "Live logical tree projection for the active preview design session.",
                "application/json",
                cancellationToken => ReadDesignResourceAsync("tree.logical", null, cancellationToken));
            resources[DesignVisualTreeResourceUri] = new McpResourceDefinition(
                DesignVisualTreeResourceUri,
                "AXSG Preview Visual Tree",
                "Live visual tree projection for the active preview design session.",
                "application/json",
                cancellationToken => ReadDesignResourceAsync("tree.visual", null, cancellationToken));
            resources[DesignOverlayCurrentResourceUri] = new McpResourceDefinition(
                DesignOverlayCurrentResourceUri,
                "AXSG Preview Overlay",
                "Current hover/selection overlay state for the active preview design session.",
                "application/json",
                cancellationToken => ReadDesignResourceAsync("overlay.current", null, cancellationToken));

            foreach (string buildUri in _state.GetDesignDocumentBuildUris())
            {
                string resourceUri = BuildWorkspaceByBuildUriResourceUri(buildUri);
                resources[resourceUri] = new McpResourceDefinition(
                    resourceUri,
                    "AXSG Preview Workspace By Build URI",
                    "AXSG preview design workspace snapshot scoped to a specific build URI.",
                    "application/json",
                    cancellationToken => ReadDesignResourceAsync("workspace.byBuildUri", new { buildUri }, cancellationToken));
            }
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
        await RefreshDesignResourcesAsync(cancellationToken).ConfigureAwait(false);
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
        _state.ClearDesignDocuments();
        await NotifyPreviewLifecycleAsync(cancellationToken, includeStatus: true, includeEvents: true).ConfigureAwait(false);
        return new
        {
            stopped = true
        };
    }

    private async ValueTask<object?> HandleDesignToolAsync(string operation, JsonObject? arguments, CancellationToken cancellationToken)
    {
        try
        {
            JsonElement result = await ReadDesignResourceElementAsync(operation, arguments, cancellationToken).ConfigureAwait(false);
            await RefreshDesignResourcesAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<object>(result.GetRawText(), JsonRpcSerializer.DefaultOptions);
        }
        catch (InvalidOperationException ex)
        {
            throw new McpToolException(new { message = ex.Message });
        }
    }

    private async ValueTask<object?> ReadDesignResourceAsync(string operation, object? payload, CancellationToken cancellationToken)
    {
        JsonElement result = await ReadDesignResourceElementAsync(
            operation,
            payload is JsonObject jsonObject ? jsonObject : JsonSerializer.SerializeToNode(payload, JsonRpcSerializer.DefaultOptions) as JsonObject,
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<object>(result.GetRawText(), JsonRpcSerializer.DefaultOptions);
    }

    private async ValueTask<JsonElement> ReadDesignResourceElementAsync(
        string operation,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        AxsgPreviewHostResponseEnvelope response = await _router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.DesignCommand,
                CreateRequestId(),
                SerializeArguments(new JsonObject
                {
                    ["operation"] = operation,
                    ["arguments"] = arguments ?? new JsonObject()
                })),
            cancellationToken).ConfigureAwait(false);

        if (!response.Ok)
        {
            string error = string.IsNullOrWhiteSpace(response.Error)
                ? "Preview design request failed."
                : response.Error;
            throw new InvalidOperationException(error);
        }

        using JsonDocument document = JsonDocument.Parse(response.Payload?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private async Task RefreshDesignResourcesAsync(CancellationToken cancellationToken)
    {
        if (!_state.HasActiveSession)
        {
            _state.ClearDesignDocuments();
            return;
        }

        try
        {
            JsonElement workspace = await ReadDesignResourceElementAsync("workspace.current", null, cancellationToken).ConfigureAwait(false);
            List<string?> buildUris = new();
            if (workspace.TryGetProperty("documents", out JsonElement documentsElement) &&
                documentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement document in documentsElement.EnumerateArray())
                {
                    buildUris.Add(
                        document.TryGetProperty("buildUri", out JsonElement buildUriElement) &&
                        buildUriElement.ValueKind == JsonValueKind.String
                            ? buildUriElement.GetString()
                            : document.TryGetProperty("BuildUri", out buildUriElement) &&
                              buildUriElement.ValueKind == JsonValueKind.String
                                ? buildUriElement.GetString()
                                : null);
                }
            }

            _state.UpdateDesignDocuments(buildUris);
        }
        catch
        {
            _state.ClearDesignDocuments();
        }
    }

    internal static string BuildWorkspaceByBuildUriResourceUri(string buildUri)
    {
        return DesignWorkspaceByBuildUriResourcePrefix + Uri.EscapeDataString(buildUri);
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
                _ = RefreshDesignResourcesAsync(CancellationToken.None);
                _ = NotifyPreviewLifecycleAsync(CancellationToken.None, includeStatus: true, includeEvents: true);
                break;

            case AxsgPreviewHostProtocol.HostExitedEvent:
                AxsgPreviewHostHostExitedEventPayload hostExitedPayload = DeserializePayload<AxsgPreviewHostHostExitedEventPayload>(envelope.Payload, "host exited event");
                _state.MarkHostExited(hostExitedPayload.ExitCode);
                _state.ClearDesignDocuments();
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
