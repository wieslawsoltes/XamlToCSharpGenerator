using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using XamlToCSharpGenerator.PreviewerHost.Protocol;

namespace XamlToCSharpGenerator.PreviewerHost;

internal sealed record PreviewSessionStartRequest(
    string DotNetCommand,
    string HostAssemblyPath,
    string PreviewerToolPath,
    string RuntimeConfigPath,
    string DepsFilePath,
    string SourceAssemblyPath,
    string XamlFileProjectPath,
    string XamlText);

internal sealed record PreviewSessionStartResult(
    string PreviewUrl,
    int TransportPort,
    int PreviewPort,
    Guid SessionId);

internal sealed record PreviewUpdateResult(
    bool Succeeded,
    string? Error,
    PreviewExceptionDetails? Exception);

internal sealed class PreviewSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly TimeSpan _startupTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly TaskCompletionSource<TcpClient> _clientAccepted = CreateCompletionSource<TcpClient>();
    private readonly TaskCompletionSource<string> _previewUrlAvailable = CreateCompletionSource<string>();

    private TcpListener? _listener;
    private Process? _hostProcess;
    private Task? _acceptLoopTask;
    private Task? _readLoopTask;
    private AvaloniaDesignerTransport? _transport;
    private string? _sourceAssemblyPath;
    private string? _xamlFileProjectPath;

    public event Action<string>? Log;

    public event Action<string>? PreviewUrlPublished;

    public event Action<PreviewUpdateResult>? UpdateCompleted;

    public event Action<int?>? HostExited;

    public async Task<PreviewSessionStartResult> StartAsync(
        PreviewSessionStartRequest request,
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
            var transportPort = StartListener();
            var previewPort = AllocateTcpPort();
            _sourceAssemblyPath = request.SourceAssemblyPath;
            _xamlFileProjectPath = NormalizeProjectPath(request.XamlFileProjectPath);

            StartHostProcess(request, transportPort, previewPort);

            var acceptedClient = await WaitWithTimeoutAsync(
                _clientAccepted.Task,
                _startupTimeout,
                linkedCancellationToken.Token,
                "Timed out waiting for the Avalonia previewer to connect.").ConfigureAwait(false);

            _transport = new AvaloniaDesignerTransport(acceptedClient.GetStream());
            _readLoopTask = Task.Run(() => RunReadLoopAsync(_transport, _disposeSource.Token), _disposeSource.Token);

            await _transport.SendUpdateXamlAsync(
                request.XamlText,
                _sourceAssemblyPath,
                _xamlFileProjectPath,
                linkedCancellationToken.Token).ConfigureAwait(false);

            var previewUrl = await WaitWithTimeoutAsync(
                _previewUrlAvailable.Task,
                _startupTimeout,
                linkedCancellationToken.Token,
                "Timed out waiting for the Avalonia preview URL.").ConfigureAwait(false);

            return new PreviewSessionStartResult(
                previewUrl,
                transportPort,
                previewPort,
                GetSessionId());
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
        var transport = _transport ?? throw new InvalidOperationException("Preview session is not connected.");
        var sourceAssemblyPath = _sourceAssemblyPath ?? throw new InvalidOperationException("Source assembly path is unavailable.");
        var xamlFileProjectPath = _xamlFileProjectPath ?? throw new InvalidOperationException("XAML project path is unavailable.");

        await transport.SendUpdateXamlAsync(
            xamlText,
            sourceAssemblyPath,
            xamlFileProjectPath,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeSource.IsCancellationRequested)
        {
            return;
        }

        _disposeSource.Cancel();

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
        }

        _disposeSource.Dispose();
    }

    private static void ValidateRequest(PreviewSessionStartRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.DotNetCommand);
        ArgumentException.ThrowIfNullOrEmpty(request.HostAssemblyPath);
        ArgumentException.ThrowIfNullOrEmpty(request.PreviewerToolPath);
        ArgumentException.ThrowIfNullOrEmpty(request.RuntimeConfigPath);
        ArgumentException.ThrowIfNullOrEmpty(request.DepsFilePath);
        ArgumentException.ThrowIfNullOrEmpty(request.SourceAssemblyPath);
        ArgumentException.ThrowIfNullOrEmpty(request.XamlFileProjectPath);

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

    private int StartListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listener = listener;
        _acceptLoopTask = Task.Run(() => AcceptClientAsync(listener, _disposeSource.Token), _disposeSource.Token);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task AcceptClientAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _clientAccepted.TrySetResult(client);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or SocketException)
        {
            if (!_disposeSource.IsCancellationRequested)
            {
                _clientAccepted.TrySetException(ex);
            }
        }
    }

    private void StartHostProcess(PreviewSessionStartRequest request, int transportPort, int previewPort)
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
                _clientAccepted.TrySetException(exception);
                _previewUrlAvailable.TrySetException(exception);
                HostExited?.Invoke(exitCode);
            }
            catch
            {
                // Ignore host exit observer failures.
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the Avalonia previewer host process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _hostProcess = process;
    }

    private async Task RunReadLoopAsync(AvaloniaDesignerTransport transport, CancellationToken cancellationToken)
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
                            _previewUrlAvailable.TrySetResult(htmlTransportStarted.Uri);
                            PreviewUrlPublished?.Invoke(htmlTransportStarted.Uri);
                        }
                        break;

                    case UpdateXamlResultPayload updateResult:
                        UpdateCompleted?.Invoke(new PreviewUpdateResult(
                            string.IsNullOrWhiteSpace(updateResult.Error),
                            updateResult.Error,
                            updateResult.Exception));
                        break;

                    case StartDesignerSessionPayload sessionStarted:
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
            _previewUrlAvailable.TrySetException(ex);
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

    private static int AllocateTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
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

    private Guid _sessionId;
}
