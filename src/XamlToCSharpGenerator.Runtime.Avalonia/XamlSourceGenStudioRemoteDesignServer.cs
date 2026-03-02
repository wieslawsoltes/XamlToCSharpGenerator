using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioRemoteDesignServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly SourceGenStudioOptions _options;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private bool _started;
    private string? _lastError;
    private int _activeClientCount;

    public XamlSourceGenStudioRemoteDesignServer(SourceGenStudioOptions options)
    {
        _options = options.Clone();
    }

    public bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                return _started;
            }
        }
    }

    public void Start()
    {
        bool publishDisabledStatus;
        bool startedSuccessfully;
        string? startError = null;

        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            publishDisabledStatus = !_options.EnableRemoteDesign;
            if (publishDisabledStatus)
            {
                _lastError = null;
            }
            else
            {
                TcpListener? listener = null;
                CancellationTokenSource? cts = null;

                try
                {
                    var address = ResolveBindAddress(_options.RemoteHost);
                    listener = new TcpListener(address, _options.RemotePort);
                    listener.Start();

                    cts = new CancellationTokenSource();
                    var acceptLoop = Task.Run(() => RunAcceptLoopAsync(cts.Token));

                    _listener = listener;
                    _cts = cts;
                    _acceptLoop = acceptLoop;
                    _started = true;
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    try
                    {
                        listener?.Stop();
                    }
                    catch
                    {
                        // Best effort startup cleanup.
                    }

                    try
                    {
                        cts?.Cancel();
                    }
                    catch
                    {
                        // Best effort startup cleanup.
                    }

                    cts?.Dispose();

                    _listener = null;
                    _cts = null;
                    _acceptLoop = null;
                    _started = false;
                    _lastError = ex.Message;
                }
            }

            startedSuccessfully = _started;
            startError = _lastError;
        }

        if (publishDisabledStatus)
        {
            PublishStatus(isListening: false, lastError: null);
            return;
        }

        if (startedSuccessfully)
        {
            PublishStatus(isListening: true, lastError: null);
            return;
        }

        PublishStatus(isListening: false, lastError: startError);
    }

    public void Stop()
    {
        Task? acceptLoop;
        CancellationTokenSource? cts;
        TcpListener? listener;

        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            acceptLoop = _acceptLoop;
            _acceptLoop = null;
            cts = _cts;
            _cts = null;
            listener = _listener;
            _listener = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
            // Best effort shutdown.
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
            // Best effort shutdown.
        }

        try
        {
            if (acceptLoop is not null && !acceptLoop.IsCompleted)
            {
                acceptLoop.Wait(millisecondsTimeout: 500);
            }
        }
        catch
        {
            // Best effort shutdown.
        }

        cts?.Dispose();

        PublishStatus(isListening: false, lastError: null);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunAcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                var listener = _listener;
                if (listener is null)
                {
                    return;
                }

                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                return;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                _lastError = ex.Message;
                PublishStatus(isListening: true, _lastError);
                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeClientCount);
        PublishStatus(isListening: true, lastError: null);

        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16 * 1024, leaveOpen: true)
            {
                AutoFlush = true
            })
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var readTask = reader.ReadLineAsync();
                    var line = readTask is null
                        ? null
                        : await readTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    StudioRemoteRequest? request;
                    try
                    {
                        request = ParseRequest(line);
                    }
                    catch (Exception ex)
                    {
                        await WriteResponseAsync(
                            writer,
                            new StudioRemoteResponse(
                                Ok: false,
                                Command: "invalid",
                                RequestId: null,
                                Error: "Invalid JSON request: " + ex.Message,
                                Payload: null),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var response = await ProcessRequestAsync(request, cancellationToken).ConfigureAwait(false);
                    await WriteResponseAsync(writer, response, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            PublishStatus(isListening: true, _lastError);
        }
        finally
        {
            Interlocked.Decrement(ref _activeClientCount);
            PublishStatus(isListening: true, lastError: null);
        }
    }

    private async ValueTask<StudioRemoteResponse> ProcessRequestAsync(
        StudioRemoteRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case "ping":
                return new StudioRemoteResponse(
                    Ok: true,
                    Command: request.Command,
                    RequestId: request.RequestId,
                    Error: null,
                    Payload: new
                    {
                        pong = true,
                        utcNow = DateTimeOffset.UtcNow
                    });

            case "getstatus":
                return new StudioRemoteResponse(
                    Ok: true,
                    Command: request.Command,
                    RequestId: request.RequestId,
                    Error: null,
                    Payload: BuildStatusPayload(XamlSourceGenStudioManager.GetStatusSnapshot()));

            case "getworkspace":
            {
                var buildUri = NormalizeOptionalText(TryGetString(request.Payload, "buildUri"));
                var search = NormalizeOptionalText(TryGetString(request.Payload, "search"));
                var workspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search);
                return new StudioRemoteResponse(
                    Ok: true,
                    Command: request.Command,
                    RequestId: request.RequestId,
                    Error: null,
                    Payload: BuildWorkspacePayload(workspace, XamlSourceGenStudioManager.GetStatusSnapshot()));
            }

            case "selectdocument":
            {
                var buildUri = NormalizeOptionalText(TryGetString(request.Payload, "buildUri"));
                if (string.IsNullOrWhiteSpace(buildUri))
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: request.RequestId,
                        Error: "buildUri is required.",
                        Payload: null);
                }

                var documentExists = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
                    .Any(candidate => string.Equals(candidate.BuildUri, buildUri, StringComparison.OrdinalIgnoreCase));
                if (!documentExists)
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: request.RequestId,
                        Error: "No registered document matches buildUri '" + buildUri + "'.",
                        Payload: null);
                }

                XamlSourceGenHotDesignTool.SelectDocument(buildUri);
                var workspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search: null);
                return new StudioRemoteResponse(
                    Ok: true,
                    Command: request.Command,
                    RequestId: request.RequestId,
                    Error: null,
                    Payload: BuildWorkspacePayload(workspace, XamlSourceGenStudioManager.GetStatusSnapshot()));
            }

            case "selectelement":
            {
                var buildUri = NormalizeOptionalText(TryGetString(request.Payload, "buildUri"));
                var elementId = NormalizeOptionalText(TryGetString(request.Payload, "elementId"));
                if (string.IsNullOrWhiteSpace(elementId))
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: request.RequestId,
                        Error: "elementId is required.",
                        Payload: null);
                }

                var activeBuildUri = buildUri;
                if (string.IsNullOrWhiteSpace(activeBuildUri))
                {
                    var current = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot();
                    activeBuildUri = current.ActiveBuildUri;
                }

                if (string.IsNullOrWhiteSpace(activeBuildUri))
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: request.RequestId,
                        Error: "No active document is available for element selection.",
                        Payload: null);
                }

                var documentExists = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
                    .Any(candidate => string.Equals(candidate.BuildUri, activeBuildUri, StringComparison.OrdinalIgnoreCase));
                if (!documentExists)
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: request.RequestId,
                        Error: "No registered document matches buildUri '" + activeBuildUri + "'.",
                        Payload: null);
                }

                var workspaceBeforeSelection = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(activeBuildUri, search: null);
                if (!ContainsElementId(workspaceBeforeSelection.Elements, elementId))
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: request.RequestId,
                        Error: "No element with id '" + elementId + "' exists in buildUri '" + activeBuildUri + "'.",
                        Payload: null);
                }

                XamlSourceGenHotDesignTool.SelectElement(activeBuildUri, elementId);
                var workspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(activeBuildUri, search: null);
                return new StudioRemoteResponse(
                    Ok: true,
                    Command: request.Command,
                    RequestId: request.RequestId,
                    Error: null,
                    Payload: BuildWorkspacePayload(workspace, XamlSourceGenStudioManager.GetStatusSnapshot()));
            }

            case "applydocumenttext":
            {
                var buildUri = NormalizeOptionalText(TryGetString(request.Payload, "buildUri"));
                var xamlText = TryGetString(request.Payload, "xamlText");
                var requestId = request.RequestId;

                if (string.IsNullOrWhiteSpace(buildUri))
                {
                    buildUri = NormalizeOptionalText(XamlSourceGenHotDesignTool.GetWorkspaceSnapshot().ActiveBuildUri);
                }

                if (string.IsNullOrWhiteSpace(buildUri))
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: requestId,
                        Error: "buildUri is required.",
                        Payload: null);
                }

                if (xamlText is null)
                {
                    return new StudioRemoteResponse(
                        Ok: false,
                        Command: request.Command,
                        RequestId: requestId,
                        Error: "xamlText is required.",
                        Payload: null);
                }

                var document = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
                    .FirstOrDefault(candidate => string.Equals(candidate.BuildUri, buildUri, StringComparison.OrdinalIgnoreCase));

                var applyRequest = new SourceGenStudioUpdateRequest
                {
                    RequestId = requestId,
                    BuildUri = buildUri,
                    TargetType = document?.RootType,
                    TargetTypeName = document?.RootType.FullName,
                    XamlText = xamlText
                };

                var applyResult = await XamlSourceGenStudioManager
                    .ApplyUpdateAsync(applyRequest, cancellationToken)
                    .ConfigureAwait(false);
                var workspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search: null);
                return new StudioRemoteResponse(
                    Ok: applyResult.Succeeded,
                    Command: request.Command,
                    RequestId: requestId,
                    Error: applyResult.Succeeded ? null : applyResult.Message,
                    Payload: new
                    {
                        applyResult = BuildStudioUpdateResultPayload(applyResult),
                        workspace = BuildWorkspacePayload(workspace, XamlSourceGenStudioManager.GetStatusSnapshot())
                    });
            }

            default:
                return new StudioRemoteResponse(
                    Ok: false,
                    Command: request.Command,
                    RequestId: request.RequestId,
                    Error: "Unsupported command '" + request.Command + "'.",
                    Payload: null);
        }
    }

    private static async ValueTask WriteResponseAsync(
        StreamWriter writer,
        StudioRemoteResponse response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private void PublishStatus(bool isListening, string? lastError)
    {
        var enabled = _options.EnableRemoteDesign;
        XamlSourceGenStudioManager.UpdateRemoteStatus(
            new SourceGenStudioRemoteStatus(
                IsEnabled: enabled,
                IsListening: enabled && isListening && IsStarted,
                Host: _options.RemoteHost,
                Port: _options.RemotePort,
                ActiveClientCount: Math.Max(0, Volatile.Read(ref _activeClientCount)),
                LastError: string.IsNullOrWhiteSpace(lastError) ? null : lastError,
                VncEndpoint: _options.VncEndpoint,
                UpdatedAtUtc: DateTimeOffset.UtcNow));
    }

    private static IPAddress ResolveBindAddress(string host)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            string.Equals(host, "0.0.0.0", StringComparison.Ordinal))
        {
            return IPAddress.Any;
        }

        if (string.Equals(host, "::", StringComparison.Ordinal))
        {
            return IPAddress.IPv6Any;
        }

        if (IPAddress.TryParse(host, out var parsed))
        {
            return parsed;
        }

        var addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("Could not resolve remote design host '" + host + "'.");
        }

        return addresses[0];
    }

    private static StudioRemoteRequest ParseRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Request payload must be a JSON object.");
        }

        var command = TryGetString(root, "command") ?? TryGetString(root, "messageType");
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new JsonException("Request command is missing.");
        }

        var requestId = NormalizeOptionalText(TryGetString(root, "requestId"));
        var payload = root.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : root.Clone();
        return new StudioRemoteRequest(
            command.Trim().ToLowerInvariant(),
            requestId,
            payload);
    }

    private static object BuildStatusPayload(SourceGenStudioStatusSnapshot snapshot)
    {
        return new
        {
            snapshot.IsEnabled,
            snapshot.SessionId,
            currentState = snapshot.CurrentState.ToString(),
            snapshot.RegisteredDocumentCount,
            snapshot.ActiveScopeCount,
            options = new
            {
                snapshot.Options.PersistChangesToSource,
                waitMode = snapshot.Options.WaitMode.ToString(),
                updateTimeout = snapshot.Options.UpdateTimeout,
                fallbackPolicy = snapshot.Options.FallbackPolicy.ToString(),
                snapshot.Options.ShowOverlayIndicator,
                snapshot.Options.EnableExternalWindow,
                snapshot.Options.AutoOpenStudioWindowOnStartup,
                snapshot.Options.EnableTracing,
                snapshot.Options.MaxOperationHistoryEntries,
                snapshot.Options.EnableRemoteDesign,
                snapshot.Options.RemoteHost,
                snapshot.Options.RemotePort,
                snapshot.Options.VncEndpoint,
                snapshot.Options.AutoOpenVncViewerOnDesktop
            },
            remote = new
            {
                snapshot.Remote.IsEnabled,
                snapshot.Remote.IsListening,
                snapshot.Remote.Host,
                snapshot.Remote.Port,
                snapshot.Remote.ActiveClientCount,
                snapshot.Remote.LastError,
                snapshot.Remote.VncEndpoint,
                snapshot.Remote.UpdatedAtUtc
            },
            scopes = snapshot.Scopes.Select(static scope => new
            {
                scopeKind = scope.ScopeKind.ToString(),
                scope.Id,
                scope.DisplayName,
                targetTypeName = scope.TargetType?.FullName,
                scope.BuildUri
            }),
            operations = snapshot.Operations.Select(static operation => new
            {
                operation.OperationId,
                operation.RequestId,
                operation.CorrelationId,
                state = operation.State.ToString(),
                operation.StartedAtUtc,
                operation.CompletedAtUtc,
                request = new
                {
                    operation.Request.BuildUri,
                    operation.Request.TargetTypeName,
                    scopeKind = operation.Request.ScopeKind.ToString(),
                    operation.Request.ScopeId
                },
                result = operation.Result is null ? null : BuildStudioUpdateResultPayload(operation.Result),
                diagnostics = operation.Diagnostics
            })
        };
    }

    private static object BuildStudioUpdateResultPayload(SourceGenStudioUpdateResult result)
    {
        return new
        {
            result.Succeeded,
            result.Message,
            result.OperationId,
            result.RequestId,
            result.CorrelationId,
            state = result.State.ToString(),
            result.SourcePersisted,
            result.LocalUpdateObserved,
            result.RuntimeFallbackApplied,
            result.BuildUri,
            targetTypeName = result.TargetType?.FullName,
            error = result.Error?.Message,
            result.Diagnostics
        };
    }

    private static object BuildWorkspacePayload(
        SourceGenHotDesignWorkspaceSnapshot workspace,
        SourceGenStudioStatusSnapshot studioStatus)
    {
        return new
        {
            status = new
            {
                workspace.Status.IsEnabled,
                workspace.Status.RegisteredDocumentCount,
                workspace.Status.RegisteredApplierCount,
                options = new
                {
                    workspace.Status.Options.PersistChangesToSource,
                    workspace.Status.Options.UseMinimalDiffPersistence,
                    workspace.Status.Options.WaitForHotReload,
                    workspace.Status.Options.HotReloadWaitTimeout,
                    workspace.Status.Options.FallbackToRuntimeApplyOnTimeout,
                    workspace.Status.Options.EnableTracing,
                    workspace.Status.Options.MaxHistoryEntries
                }
            },
            remote = new
            {
                studioStatus.Remote.IsEnabled,
                studioStatus.Remote.IsListening,
                studioStatus.Remote.Host,
                studioStatus.Remote.Port,
                studioStatus.Remote.ActiveClientCount,
                studioStatus.Remote.LastError,
                studioStatus.Remote.VncEndpoint,
                studioStatus.Remote.UpdatedAtUtc
            },
            mode = workspace.Mode.ToString(),
            propertyFilterMode = workspace.PropertyFilterMode.ToString(),
            panels = new
            {
                workspace.Panels.ToolbarVisible,
                workspace.Panels.ElementsVisible,
                workspace.Panels.ToolboxVisible,
                workspace.Panels.CanvasVisible,
                workspace.Panels.PropertiesVisible
            },
            canvas = new
            {
                workspace.Canvas.Zoom,
                workspace.Canvas.FormFactor,
                workspace.Canvas.Width,
                workspace.Canvas.Height,
                workspace.Canvas.DarkTheme
            },
            workspace.ActiveBuildUri,
            workspace.SelectedElementId,
            workspace.CanUndo,
            workspace.CanRedo,
            workspace.CurrentXamlText,
            documents = workspace.Documents.Select(static document => new
            {
                rootTypeName = document.RootType.FullName,
                document.BuildUri,
                document.SourcePath,
                document.LiveInstanceCount,
                documentRole = document.DocumentRole.ToString(),
                artifactKind = document.ArtifactKind.ToString(),
                document.ScopeHints
            }),
            elements = BuildElementPayload(workspace.Elements),
            properties = workspace.Properties.Select(static property => new
            {
                property.Name,
                property.Value,
                property.TypeName,
                property.IsSet,
                property.IsAttached,
                property.IsMarkupExtension,
                quickSets = property.QuickSets.Select(static quickSet => new
                {
                    quickSet.Label,
                    quickSet.Value
                }),
                property.Category,
                property.Source,
                property.OwnerTypeName,
                property.EditorKind,
                property.IsPinned,
                property.IsReadOnly,
                property.CanReset,
                property.EnumOptions
            }),
            toolbox = workspace.Toolbox.Select(static category => new
            {
                category.Name,
                items = category.Items.Select(static item => new
                {
                    item.Name,
                    item.DisplayName,
                    item.Category,
                    item.XamlSnippet,
                    item.IsProjectControl,
                    item.Tags
                })
            })
        };
    }

    private static IReadOnlyList<object> BuildElementPayload(IReadOnlyList<SourceGenHotDesignElementNode> elements)
    {
        if (elements.Count == 0)
        {
            return Array.Empty<object>();
        }

        var output = new List<object>(elements.Count);
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            output.Add(new
            {
                element.Id,
                element.DisplayName,
                element.TypeName,
                element.XamlName,
                element.Classes,
                element.Depth,
                element.IsSelected,
                element.Line,
                element.IsExpanded,
                element.DescendantCount,
                element.SourceBuildUri,
                element.SourceElementId,
                element.IsLive,
                children = BuildElementPayload(element.Children)
            });
        }

        return output;
    }

    private static bool ContainsElementId(IReadOnlyList<SourceGenHotDesignElementNode> elements, string elementId)
    {
        if (elements.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            if (string.Equals(element.Id, elementId, StringComparison.Ordinal))
            {
                return true;
            }

            if (ContainsElementId(element.Children, elementId))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private sealed record StudioRemoteRequest(
        string Command,
        string? RequestId,
        JsonElement Payload);

    private sealed record StudioRemoteResponse(
        bool Ok,
        string Command,
        string? RequestId,
        string? Error,
        object? Payload);
}
