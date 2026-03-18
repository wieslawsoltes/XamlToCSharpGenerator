using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Preview;
using XamlToCSharpGenerator.RemoteProtocol.Studio;
using XamlToCSharpGenerator.PreviewerHost.Protocol;

namespace XamlToCSharpGenerator.PreviewerHost;

internal sealed class PreviewSession : IPreviewHostSession
{
    private const int MaxStartupAttempts = 3;
    private const string DefaultDesignHost = "127.0.0.1";
    private static readonly string[] PreviewPortBindFailureMarkers =
    [
        "address already in use",
        "only one usage of each socket address",
        "eaddrinuse"
    ];

    private readonly object _sync = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly TimeSpan _startupTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly PreviewUpdateResultTracker _pendingUpdateResults = new();

    private TcpListener? _listener;
    private Process? _hostProcess;
    private Task? _acceptLoopTask;
    private Task? _readLoopTask;
    private AvaloniaDesignerTransport? _transport;
    private StudioRemoteDesignClient? _designClient;
    private string? _sourceAssemblyPath;
    private string? _xamlFileProjectPath;
    private double? _previewWidth;
    private double? _previewHeight;
    private double? _previewScale;
    private bool _sessionStarted;

    public event Action<string>? Log;

    public event Action<string>? PreviewUrlPublished;

    public event Action<AxsgPreviewHostUpdateResultEventPayload>? UpdateCompleted;

    public event Action<int?>? HostExited;

    public async Task<AxsgPreviewHostStartResponse> StartAsync(
        AxsgPreviewHostStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ThrowIfDisposed();
        ValidateRequest(request);

        var linkedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeSource.Token);
        try
        {
            _sourceAssemblyPath = request.SourceAssemblyPath;
            _xamlFileProjectPath = NormalizeProjectPath(request.XamlFileProjectPath);
            _previewWidth = request.PreviewWidth;
            _previewHeight = request.PreviewHeight;
            _previewScale = request.PreviewScale;

            Exception? lastException = null;

            for (var attempt = 1; attempt <= MaxStartupAttempts; attempt++)
            {
                var clientAccepted = CreateCompletionSource<TcpClient>();
                var previewUrlAvailable = CreateCompletionSource<string>();
                var designerSessionStarted = CreateCompletionSource<string>();
                var hostDiagnostics = new StringBuilder();
                TcpListener? previewPortReservation = null;
                TcpListener? designPortReservation = null;

                try
                {
                    var transportPort = StartListener(clientAccepted);
                    previewPortReservation = ReserveTcpPort();
                    var previewPort = GetListenerPort(previewPortReservation);
                    designPortReservation = ReserveTcpPort();
                    var designPort = GetListenerPort(designPortReservation);

                    StartHostProcess(
                        request,
                        transportPort,
                        previewPort,
                        previewPortReservation,
                        DefaultDesignHost,
                        designPort,
                        designPortReservation,
                        clientAccepted,
                        previewUrlAvailable,
                        designerSessionStarted,
                        hostDiagnostics);
                    previewPortReservation = null;
                    designPortReservation = null;

                    var acceptedClient = await WaitWithTimeoutAsync(
                        clientAccepted.Task,
                        _startupTimeout,
                        linkedCancellationToken.Token,
                        "Timed out waiting for the Avalonia previewer to connect.").ConfigureAwait(false);

                    _transport = new AvaloniaDesignerTransport(acceptedClient.GetStream());
                    _readLoopTask = Task.Run(
                        () => RunReadLoopAsync(_transport, previewUrlAvailable, designerSessionStarted, _disposeSource.Token),
                        _disposeSource.Token);

                    await WaitWithTimeoutAsync(
                        designerSessionStarted.Task,
                        _startupTimeout,
                        linkedCancellationToken.Token,
                        "Timed out waiting for the Avalonia preview session to start.").ConfigureAwait(false);

                    _designClient = await StudioRemoteDesignClient.ConnectAsync(
                        DefaultDesignHost,
                        designPort,
                        _startupTimeout,
                        linkedCancellationToken.Token).ConfigureAwait(false);

                    var previewUrl = await CompleteInitialPreviewStartupAsync(
                        cancellationToken => _transport.SendInitialClientBootstrapAsync(
                            _previewWidth,
                            _previewHeight,
                            _previewScale,
                            cancellationToken),
                        cancellationToken =>
                        {
                            _pendingUpdateResults.EnqueueFireAndForget();
                            return SendUpdateXamlAsync(request.XamlText, cancellationToken);
                        },
                        previewUrlAvailable.Task,
                        _startupTimeout,
                        linkedCancellationToken.Token).ConfigureAwait(false);

                    _sessionStarted = true;

                    return new AxsgPreviewHostStartResponse(
                        previewUrl,
                        transportPort,
                        previewPort,
                        GetSessionId());
                }
                catch (Exception ex) when (ShouldRetryStartup(ex, hostDiagnostics.ToString(), attempt, MaxStartupAttempts))
                {
                    lastException = ex;
                    Log?.Invoke(
                        "[previewer] preview startup lost the HTML port binding race; retrying with a new reserved port.");
                    await CleanupStartupAttemptAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await CleanupStartupAttemptAsync().ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    previewPortReservation?.Stop();
                    designPortReservation?.Stop();
                }
            }

            throw lastException ?? new InvalidOperationException("Preview startup failed.");
        }
        finally
        {
            linkedCancellationToken.Dispose();
        }
    }

    public async Task UpdateAsync(string xamlText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(xamlText);

        ThrowIfDisposed();
        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingUpdateResults.EnqueueFireAndForget();
            await SendUpdateXamlAsync(xamlText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<AxsgPreviewHostHotReloadResponse> HotReloadAsync(
        string xamlText,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(xamlText);

        ThrowIfDisposed();
        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TaskCompletionSource<AxsgPreviewHostHotReloadResponse> completionSource = _pendingUpdateResults.EnqueueHotReload();
            await SendUpdateXamlAsync(xamlText, cancellationToken).ConfigureAwait(false);

            try
            {
                return timeout.HasValue
                    ? await completionSource.Task.WaitAsync(timeout.Value, cancellationToken).ConfigureAwait(false)
                    : await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _pendingUpdateResults.RemoveHotReload(completionSource);
                return PublishPendingHotReloadFailure("Timed out waiting for preview hot reload to complete.");
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task SendInputAsync(AxsgPreviewHostInputRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ThrowIfDisposed();
        var transport = _transport ?? throw new InvalidOperationException("Preview session is not connected.");

        if (string.Equals(request.EventType, "text", StringComparison.Ordinal))
        {
            var textInputMessage = PreviewKeyboardInputMapper.CreateTextInputEvent(request);
            if (textInputMessage is null)
            {
                return;
            }

            await transport.SendTextInputEventAsync(textInputMessage, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (PreviewKeyboardInputMapper.TryCreateKeyEvent(request, out var keyEventMessage))
        {
            await transport.SendKeyEventAsync(keyEventMessage, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<JsonElement> ExecuteDesignAsync(
        AxsgPreviewHostDesignRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ThrowIfDisposed();
        StudioRemoteDesignClient client = _designClient ?? throw new InvalidOperationException("Preview design server is not connected.");
        string command = MapDesignOperation(request.Operation);
        object? payload = request.Arguments.ValueKind == JsonValueKind.Undefined
            ? new { }
            : request.Arguments;
        JsonElement response = await client.SendCommandAsync(command, payload, cancellationToken).ConfigureAwait(false);

        return request.Operation switch
        {
            "documents.selected" => BuildSelectedDocumentPayload(response),
            "element.selected" => BuildSelectedElementPayload(response),
            _ => response
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeSource.IsCancellationRequested)
        {
            return;
        }

        _disposeSource.Cancel();
        _sessionStarted = false;

        await CleanupStartupAttemptAsync().ConfigureAwait(false);

        _updateGate.Dispose();
        _disposeSource.Dispose();
    }

    private static void ValidateRequest(AxsgPreviewHostStartRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.DotNetCommand);
        ArgumentException.ThrowIfNullOrEmpty(request.HostAssemblyPath);
        ArgumentException.ThrowIfNullOrEmpty(request.PreviewerToolPath);
        ArgumentException.ThrowIfNullOrEmpty(request.RuntimeConfigPath);
        ArgumentException.ThrowIfNullOrEmpty(request.DepsFilePath);
        ArgumentException.ThrowIfNullOrEmpty(request.SourceAssemblyPath);
        ArgumentException.ThrowIfNullOrEmpty(request.XamlFileProjectPath);
        ArgumentException.ThrowIfNullOrEmpty(request.PreviewCompilerMode);

        if (!File.Exists(request.HostAssemblyPath))
        {
            throw new FileNotFoundException("Host assembly was not found.", request.HostAssemblyPath);
        }

        if (!File.Exists(request.PreviewerToolPath))
        {
            throw new FileNotFoundException("Avalonia previewer host was not found.", request.PreviewerToolPath);
        }

        if (!File.Exists(request.RuntimeConfigPath))
        {
            throw new FileNotFoundException("Host runtimeconfig.json was not found.", request.RuntimeConfigPath);
        }

        if (!File.Exists(request.DepsFilePath))
        {
            throw new FileNotFoundException("Host deps.json was not found.", request.DepsFilePath);
        }

        if (!File.Exists(request.SourceAssemblyPath))
        {
            throw new FileNotFoundException("Source assembly was not found.", request.SourceAssemblyPath);
        }
    }

    private int StartListener(TaskCompletionSource<TcpClient> clientAccepted)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listener = listener;
        _acceptLoopTask = Task.Run(
            () => AcceptClientAsync(listener, clientAccepted, _disposeSource.Token),
            _disposeSource.Token);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task AcceptClientAsync(
        TcpListener listener,
        TaskCompletionSource<TcpClient> clientAccepted,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            clientAccepted.TrySetResult(client);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or SocketException)
        {
            if (!_disposeSource.IsCancellationRequested)
            {
                clientAccepted.TrySetException(ex);
            }
        }
    }

    private void StartHostProcess(
        AxsgPreviewHostStartRequest request,
        int transportPort,
        int previewPort,
        TcpListener previewPortReservation,
        string designHost,
        int designPort,
        TcpListener designPortReservation,
        TaskCompletionSource<TcpClient> clientAccepted,
        TaskCompletionSource<string> previewUrlAvailable,
        TaskCompletionSource<string> designerSessionStarted,
        StringBuilder hostDiagnostics)
    {
        var sessionId = GetSessionId().ToString("D");
        var previewUrl = "http://127.0.0.1:" + previewPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var transportUrl = "tcp-bson://127.0.0.1:" + transportPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.DotNetCommand,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add("--runtimeconfig");
        process.StartInfo.ArgumentList.Add(request.RuntimeConfigPath);
        process.StartInfo.ArgumentList.Add("--depsfile");
        process.StartInfo.ArgumentList.Add(request.DepsFilePath);
        process.StartInfo.ArgumentList.Add(request.PreviewerToolPath);
        process.StartInfo.ArgumentList.Add("--transport");
        process.StartInfo.ArgumentList.Add(transportUrl);
        process.StartInfo.ArgumentList.Add("--session-id");
        process.StartInfo.ArgumentList.Add(sessionId);
        process.StartInfo.ArgumentList.Add("--method");
        process.StartInfo.ArgumentList.Add("html");
        process.StartInfo.ArgumentList.Add("--html-url");
        process.StartInfo.ArgumentList.Add(previewUrl);
        process.StartInfo.ArgumentList.Add("--axsg-compiler-mode");
        process.StartInfo.ArgumentList.Add(request.PreviewCompilerMode);
        if (request.PreviewWidth is > 0)
        {
            process.StartInfo.ArgumentList.Add("--axsg-preview-width");
            process.StartInfo.ArgumentList.Add(request.PreviewWidth.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (request.PreviewHeight is > 0)
        {
            process.StartInfo.ArgumentList.Add("--axsg-preview-height");
            process.StartInfo.ArgumentList.Add(request.PreviewHeight.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(request.SourceFilePath))
        {
            process.StartInfo.ArgumentList.Add("--axsg-source-file");
            process.StartInfo.ArgumentList.Add(request.SourceFilePath);
        }

        process.StartInfo.ArgumentList.Add("--axsg-source-assembly");
        process.StartInfo.ArgumentList.Add(request.SourceAssemblyPath);
        process.StartInfo.ArgumentList.Add("--axsg-xaml-project-path");
        process.StartInfo.ArgumentList.Add(request.XamlFileProjectPath);

        process.StartInfo.ArgumentList.Add("--axsg-design-host");
        process.StartInfo.ArgumentList.Add(designHost);
        process.StartInfo.ArgumentList.Add("--axsg-design-port");
        process.StartInfo.ArgumentList.Add(designPort.ToString(CultureInfo.InvariantCulture));

        process.StartInfo.ArgumentList.Add(request.HostAssemblyPath);

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Log?.Invoke("[previewer stdout] " + args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (hostDiagnostics)
                {
                    hostDiagnostics.AppendLine(args.Data);
                }

                Log?.Invoke("[previewer stderr] " + args.Data);
            }
        };

        process.Exited += (_, _) =>
        {
            try
            {
                int? exitCode = process.HasExited ? process.ExitCode : null;
                var exception = new InvalidOperationException(
                    "Avalonia previewer host exited before the session completed." +
                    (exitCode.HasValue ? " Exit code: " + exitCode.Value + "." : string.Empty));
                clientAccepted.TrySetException(exception);
                previewUrlAvailable.TrySetException(exception);
                designerSessionStarted.TrySetException(exception);
                PublishPendingHotReloadFailure(exception.Message);
                if (_sessionStarted)
                {
                    HostExited?.Invoke(exitCode);
                }
            }
            catch
            {
                // Ignore host exit observer failures.
            }
        };

        previewPortReservation.Stop();
        designPortReservation.Stop();

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the Avalonia previewer host process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _hostProcess = process;
    }

    private async Task RunReadLoopAsync(
        AvaloniaDesignerTransport transport,
        TaskCompletionSource<string> previewUrlAvailable,
        TaskCompletionSource<string> designerSessionStarted,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await transport.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    return;
                }

                switch (message)
                {
                    case HtmlTransportStartedPayload htmlTransportStarted:
                        if (!string.IsNullOrWhiteSpace(htmlTransportStarted.Uri))
                        {
                            previewUrlAvailable.TrySetResult(htmlTransportStarted.Uri);
                            PreviewUrlPublished?.Invoke(htmlTransportStarted.Uri);
                        }
                        break;

                    case UpdateXamlResultPayload updateResult:
                        var mappedResult = new AxsgPreviewHostUpdateResultEventPayload(
                            string.IsNullOrWhiteSpace(updateResult.Error),
                            updateResult.Error,
                            MapExceptionDetails(updateResult.Exception));
                        _ = CompletePendingHotReload(
                            CreateHotReloadResponse(
                                mappedResult.Succeeded,
                                mappedResult.Error,
                                mappedResult.Exception));
                        UpdateCompleted?.Invoke(mappedResult);
                        break;

                    case StartDesignerSessionPayload sessionStarted:
                        designerSessionStarted.TrySetResult(sessionStarted.SessionId);
                        Log?.Invoke("[previewer] session started: " + sessionStarted.SessionId);
                        break;

                    case UnknownDesignerMessage unknownMessage:
                        Log?.Invoke("[previewer] ignored message " + unknownMessage.MessageType);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            Log?.Invoke("[previewer] transport failed: " + ex.Message);
            previewUrlAvailable.TrySetException(ex);
            designerSessionStarted.TrySetException(ex);
            PublishPendingHotReloadFailure(ex.Message);
        }
    }

    private static string NormalizeProjectPath(string xamlFileProjectPath)
    {
        var normalized = xamlFileProjectPath.Replace('\\', '/').Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        return normalized;
    }

    private static async Task<T> WaitWithTimeoutAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string timeoutMessage)
    {
        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }

    internal static async Task<string> CompleteInitialPreviewStartupAsync(
        Func<CancellationToken, Task> sendInitialClientBootstrapAsync,
        Func<CancellationToken, Task> sendInitialUpdateAsync,
        Task<string> previewUrlAvailable,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendInitialClientBootstrapAsync);
        ArgumentNullException.ThrowIfNull(sendInitialUpdateAsync);
        ArgumentNullException.ThrowIfNull(previewUrlAvailable);

        await sendInitialClientBootstrapAsync(cancellationToken).ConfigureAwait(false);
        string previewUrl = await WaitWithTimeoutAsync(
            previewUrlAvailable,
            startupTimeout,
            cancellationToken,
            "Timed out waiting for the Avalonia preview URL.").ConfigureAwait(false);
        await sendInitialUpdateAsync(cancellationToken).ConfigureAwait(false);
        return previewUrl;
    }

    private async Task CleanupStartupAttemptAsync()
    {
        _sessionStarted = false;
        PublishPendingHotReloadFailure("Preview session stopped before hot reload completed.");

        if (_designClient is not null)
        {
            await _designClient.DisposeAsync().ConfigureAwait(false);
            _designClient = null;
        }

        if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
        }

        if (_listener is not null)
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Best effort shutdown.
            }

            _listener = null;
        }

        if (_hostProcess is not null)
        {
            try
            {
                if (!_hostProcess.HasExited)
                {
                    _hostProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort shutdown.
            }

            _hostProcess.Dispose();
            _hostProcess = null;
        }

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // Best effort shutdown.
            }

            _acceptLoopTask = null;
        }

        if (_readLoopTask is not null)
        {
            try
            {
                await _readLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // Best effort shutdown.
            }

            _readLoopTask = null;
        }
    }

    internal static bool ShouldRetryStartup(
        Exception exception,
        string hostDiagnostics,
        int attemptNumber,
        int maxAttempts = MaxStartupAttempts)
    {
        if (attemptNumber >= maxAttempts)
        {
            return false;
        }

        if (exception is OperationCanceledException)
        {
            return false;
        }

        return IsPreviewPortBindFailure(hostDiagnostics);
    }

    internal static bool IsPreviewPortBindFailure(string hostDiagnostics)
    {
        if (string.IsNullOrWhiteSpace(hostDiagnostics))
        {
            return false;
        }

        return Array.Exists(
            PreviewPortBindFailureMarkers,
            marker => hostDiagnostics.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static TcpListener ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();
        return listener;
    }

    private static int GetListenerPort(TcpListener listener)
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void ThrowIfDisposed()
    {
        _disposeSource.Token.ThrowIfCancellationRequested();
    }

    private Guid GetSessionId()
    {
        lock (_sync)
        {
            if (_sessionId == Guid.Empty)
            {
                _sessionId = Guid.NewGuid();
            }

            return _sessionId;
        }
    }

    private static TaskCompletionSource<T> CreateCompletionSource<T>()
    {
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task SendUpdateXamlAsync(string xamlText, CancellationToken cancellationToken)
    {
        var transport = _transport ?? throw new InvalidOperationException("Preview session is not connected.");
        var xamlFileProjectPath = _xamlFileProjectPath ?? throw new InvalidOperationException("XAML project path is unavailable.");

        await transport.SendUpdateXamlAsync(
            xamlText,
            assemblyPath: null,
            xamlFileProjectPath,
            cancellationToken).ConfigureAwait(false);
    }

    private static string MapDesignOperation(string operation)
    {
        return operation switch
        {
            "workspace.current" => AxsgStudioRemoteProtocol.GetWorkspaceCommand,
            "workspace.byBuildUri" => AxsgStudioRemoteProtocol.GetWorkspaceCommand,
            "documents.selected" => AxsgStudioRemoteProtocol.GetWorkspaceCommand,
            "element.selected" => AxsgStudioRemoteProtocol.GetWorkspaceCommand,
            "tree.logical" => AxsgStudioRemoteProtocol.GetLogicalTreeCommand,
            "tree.visual" => AxsgStudioRemoteProtocol.GetVisualTreeCommand,
            "overlay.current" => AxsgStudioRemoteProtocol.GetOverlayCommand,
            "selectDocument" => AxsgStudioRemoteProtocol.SelectDocumentCommand,
            "selectElement" => AxsgStudioRemoteProtocol.SelectElementCommand,
            "selectAtPoint" => AxsgStudioRemoteProtocol.SelectAtPointCommand,
            "applyDocumentText" => AxsgStudioRemoteProtocol.ApplyDocumentTextCommand,
            "applyPropertyUpdate" => AxsgStudioRemoteProtocol.ApplyPropertyUpdateCommand,
            "insertElement" => AxsgStudioRemoteProtocol.InsertElementCommand,
            "removeElement" => AxsgStudioRemoteProtocol.RemoveElementCommand,
            "undo" => AxsgStudioRemoteProtocol.UndoCommand,
            "redo" => AxsgStudioRemoteProtocol.RedoCommand,
            "setWorkspaceMode" => AxsgStudioRemoteProtocol.SetWorkspaceModeCommand,
            "setHitTestMode" => AxsgStudioRemoteProtocol.SetHitTestModeCommand,
            "setPropertyFilterMode" => AxsgStudioRemoteProtocol.SetPropertyFilterModeCommand,
            _ => throw new InvalidOperationException("Unsupported preview design operation '" + operation + "'.")
        };
    }

    private static JsonElement BuildSelectedDocumentPayload(JsonElement workspacePayload)
    {
        string? activeBuildUri = TryGetStringProperty(workspacePayload, "activeBuildUri", "ActiveBuildUri");
        JsonElement? selectedDocument = null;
        if (workspacePayload.TryGetProperty("documents", out JsonElement documentsElement) &&
            documentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement document in documentsElement.EnumerateArray())
            {
                string? buildUri = TryGetStringProperty(document, "buildUri", "BuildUri");
                if (string.Equals(buildUri, activeBuildUri, StringComparison.OrdinalIgnoreCase))
                {
                    selectedDocument = document;
                    break;
                }
            }
        }

        JsonObject payload = new()
        {
            ["activeBuildUri"] = activeBuildUri,
            ["document"] = selectedDocument.HasValue
                ? JsonRpcNodeHelpers.CloneJsonElement(selectedDocument.Value)
                : null
        };
        return JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement BuildSelectedElementPayload(JsonElement workspacePayload)
    {
        string? activeBuildUri = TryGetStringProperty(workspacePayload, "activeBuildUri", "ActiveBuildUri");
        string? selectedElementId = TryGetStringProperty(workspacePayload, "selectedElementId", "SelectedElementId");
        JsonElement? selectedElement = null;
        if (workspacePayload.TryGetProperty("elements", out JsonElement elementsElement) &&
            elementsElement.ValueKind == JsonValueKind.Array &&
            !string.IsNullOrWhiteSpace(selectedElementId))
        {
            selectedElement = FindElementPayload(elementsElement, selectedElementId);
        }

        JsonObject payload = new()
        {
            ["activeBuildUri"] = activeBuildUri,
            ["selectedElementId"] = selectedElementId,
            ["element"] = selectedElement.HasValue
                ? JsonRpcNodeHelpers.CloneJsonElement(selectedElement.Value)
                : null
        };
        return JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement? FindElementPayload(JsonElement elements, string selectedElementId)
    {
        foreach (JsonElement element in elements.EnumerateArray())
        {
            string? elementId = TryGetStringProperty(element, "id", "Id");
            if (string.Equals(elementId, selectedElementId, StringComparison.Ordinal))
            {
                return element;
            }

            if (element.TryGetProperty("children", out JsonElement childrenElement) &&
                childrenElement.ValueKind == JsonValueKind.Array)
            {
                JsonElement? child = FindElementPayload(childrenElement, selectedElementId);
                if (child.HasValue)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static string? TryGetStringProperty(JsonElement element, params string[] propertyNames)
    {
        for (int index = 0; index < propertyNames.Length; index++)
        {
            if (element.TryGetProperty(propertyNames[index], out JsonElement propertyElement) &&
                propertyElement.ValueKind == JsonValueKind.String)
            {
                return propertyElement.GetString();
            }
        }

        return null;
    }

    private AxsgPreviewHostHotReloadResponse CompletePendingHotReload(AxsgPreviewHostHotReloadResponse response)
    {
        _pendingUpdateResults.CompleteNext(response);
        return response;
    }

    private AxsgPreviewHostHotReloadResponse PublishPendingHotReloadFailure(
        string error,
        AxsgPreviewHostExceptionDetails? exception = null)
    {
        var response = CreateHotReloadResponse(
            succeeded: false,
            error: error,
            exception: exception);
        if (_pendingUpdateResults.FailAll(response) is not null)
        {
            UpdateCompleted?.Invoke(new AxsgPreviewHostUpdateResultEventPayload(false, error, exception));
        }

        return response;
    }

    private static AxsgPreviewHostHotReloadResponse CreateHotReloadResponse(
        bool succeeded,
        string? error,
        AxsgPreviewHostExceptionDetails? exception)
    {
        return new AxsgPreviewHostHotReloadResponse(
            succeeded,
            error,
            exception,
            DateTimeOffset.UtcNow);
    }

    private static AxsgPreviewHostExceptionDetails? MapExceptionDetails(PreviewExceptionDetails? exception)
    {
        if (exception is null)
        {
            return null;
        }

        return new AxsgPreviewHostExceptionDetails(
            exception.ExceptionType,
            exception.Message,
            exception.LineNumber,
            exception.LinePosition);
    }

    private Guid _sessionId;
}
