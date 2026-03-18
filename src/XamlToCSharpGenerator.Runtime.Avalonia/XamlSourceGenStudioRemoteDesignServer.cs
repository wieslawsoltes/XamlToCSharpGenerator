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
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Studio;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioRemoteDesignServer : IDisposable
{
    private readonly object _sync = new();
    private readonly SourceGenStudioOptions _options;
    private readonly AxsgStudioRemoteCommandRouter _commandRouter = new();

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

                    AxsgStudioRemoteRequestEnvelope request;
                    try
                    {
                        request = AxsgStudioRemoteProtocol.ParseRequestLine(line);
                    }
                    catch (Exception ex)
                    {
                        await WriteResponseAsync(
                            writer,
                            AxsgStudioRemoteProtocol.CreateFailureResponse(
                                command: "invalid",
                                requestId: null,
                                error: "Invalid JSON request: " + ex.Message),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    AxsgStudioRemoteResponseEnvelope response;
                    try
                    {
                        response = await _commandRouter
                            .HandleAsync(request, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        PublishStatus(isListening: true, _lastError);
                        response = AxsgStudioRemoteProtocol.CreateFailureResponse(
                            request.Command,
                            request.RequestId,
                            ex.Message);
                    }

                    try
                    {
                        await WriteResponseAsync(writer, response, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        PublishStatus(isListening: true, _lastError);
                        Console.WriteLine("[AXSG design] Failed to write response for command '" + request.Command + "': " + ex.Message);

                        AxsgStudioRemoteResponseEnvelope fallbackResponse = AxsgStudioRemoteProtocol.CreateFailureResponse(
                            request.Command,
                            request.RequestId,
                            "Remote design response failed: " + ex.Message);

                        try
                        {
                            await WriteResponseAsync(writer, fallbackResponse, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            return;
                        }
                    }
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

    private static async ValueTask WriteResponseAsync(
        StreamWriter writer,
        AxsgStudioRemoteResponseEnvelope response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonRpcSerializer.DefaultOptions);
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

}
