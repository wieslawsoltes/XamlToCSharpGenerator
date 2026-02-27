using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class RemoteSocketTransport :
    ISourceGenHotReloadTransport,
    ISourceGenHotReloadRemoteOperationTransport
{
    internal const string RemoteEndpointEnvVarName = "AXSG_HOTRELOAD_REMOTE_ENDPOINT";
    internal const string RemoteEndpointExplicitEnvVarName = "AXSG_HOTRELOAD_REMOTE_ENDPOINT_EXPLICIT";
    private const string NoEndpointSentinel = "__AXSG_NO_ENDPOINT__";

    private readonly object _sync = new();
    private readonly object _sendSync = new();
    private IRemoteConnection? _connection;
    private Thread? _listenerThread;
    private CancellationTokenSource? _listenerCts;

    public string Name => "RemoteSocket";

    public SourceGenHotReloadTransportCapabilities Capabilities => BuildCapabilities();

    public event Action<SourceGenHotReloadRemoteUpdateRequest>? RemoteUpdateReceived;

    internal static bool HasConfiguredEndpointEnvironment()
    {
        var raw = GetConfiguredEndpointRawValue();
        return !string.IsNullOrWhiteSpace(raw) &&
               !string.Equals(raw, NoEndpointSentinel, StringComparison.Ordinal);
    }

    public SourceGenHotReloadHandshakeResult StartHandshake(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!TryResolveEndpoint(out var endpoint, out var endpointError))
        {
            return SourceGenHotReloadHandshakeResult.Failure(endpointError);
        }

        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromMilliseconds(100);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        IRemoteConnection? connection = null;
        try
        {
            connection = CreateConnection(endpoint, linkedCts.Token);
            SendHandshake(connection, endpoint, linkedCts.Token);
            ActivateConnection(connection);
            connection = null;

            return SourceGenHotReloadHandshakeResult.Success(
                "Connected to remote endpoint " + endpoint.DisplayText + ".");
        }
        catch (OperationCanceledException ex)
        {
            connection?.Dispose();
            return SourceGenHotReloadHandshakeResult.Failure(
                "Remote connection timed out for endpoint " + endpoint.DisplayText + ".",
                ex);
        }
        catch (Exception ex)
        {
            connection?.Dispose();
            return SourceGenHotReloadHandshakeResult.Failure(
                "Remote connection failed for endpoint " + endpoint.DisplayText + ": " + ex.Message,
                ex);
        }
    }

    public void PublishRemoteUpdateResult(SourceGenHotReloadRemoteUpdateResult result)
    {
        if (result is null)
        {
            return;
        }

        IRemoteConnection? connection;
        lock (_sync)
        {
            connection = _connection;
        }

        if (connection is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            messageType = "ack",
            operationId = result.OperationId,
            requestId = result.RequestId,
            correlationId = result.CorrelationId,
            state = result.State.ToString(),
            isSuccess = result.IsSuccess,
            message = result.Message,
            diagnostics = result.Diagnostics
        });

        try
        {
            lock (_sendSync)
            {
                connection.SendMessage(payload, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[AXSG.HotReload.RemoteSocket] Failed to publish ACK: " + ex.Message);
        }
    }

    public void Stop()
    {
        IRemoteConnection? connection;
        Thread? listenerThread;
        CancellationTokenSource? listenerCts;

        lock (_sync)
        {
            connection = _connection;
            listenerThread = _listenerThread;
            listenerCts = _listenerCts;
            _connection = null;
            _listenerThread = null;
            _listenerCts = null;
        }

        if (listenerCts is not null)
        {
            try
            {
                listenerCts.Cancel();
            }
            catch
            {
                // Best effort cancellation.
            }
        }

        if (connection is not null)
        {
            try
            {
                connection.Dispose();
            }
            catch
            {
                // Best effort socket cleanup only.
            }
        }

        if (listenerThread is not null && listenerThread.IsAlive)
        {
            try
            {
                listenerThread.Join(millisecondsTimeout: 250);
            }
            catch
            {
                // Best effort listener shutdown only.
            }
        }

        listenerCts?.Dispose();
    }

    private static SourceGenHotReloadTransportCapabilities BuildCapabilities()
    {
        if (!TryResolveEndpoint(out _, out var endpointError))
        {
            return new SourceGenHotReloadTransportCapabilities(
                isSupported: false,
                supportsMetadataUpdates: false,
                supportsRemoteConnection: true,
                requiresEndpointConfiguration: true,
                endpointError);
        }

        return new SourceGenHotReloadTransportCapabilities(
            isSupported: true,
            supportsMetadataUpdates: false,
            supportsRemoteConnection: true,
            requiresEndpointConfiguration: true,
            "Remote endpoint is configured.");
    }

    private void ActivateConnection(IRemoteConnection connection)
    {
        Stop();

        lock (_sync)
        {
            _connection = connection;
            _listenerCts = new CancellationTokenSource();
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "AXSG-HotReload-RemoteSocket"
            };
            _listenerThread.Start();
        }
    }

    private void ListenLoop()
    {
        IRemoteConnection? connection;
        CancellationToken token;

        lock (_sync)
        {
            connection = _connection;
            token = _listenerCts?.Token ?? CancellationToken.None;
        }

        if (connection is null)
        {
            return;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!connection.TryReceiveMessage(out var message, token))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (TryParseApplyRequest(message, out var request))
                {
                    try
                    {
                        RemoteUpdateReceived?.Invoke(request);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[AXSG.HotReload.RemoteSocket] Remote update callback failed: " + ex.Message);
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
            Debug.WriteLine("[AXSG.HotReload.RemoteSocket] Listener failed: " + ex.Message);
        }
    }

    private static IRemoteConnection CreateConnection(RemoteEndpoint endpoint, CancellationToken cancellationToken)
    {
        switch (endpoint.Protocol)
        {
            case RemoteEndpointProtocol.Tcp:
                return TcpLineConnection.Connect(endpoint.Host, endpoint.Port, cancellationToken);
            case RemoteEndpointProtocol.WebSocket:
                if (endpoint.Uri is null)
                {
                    throw new InvalidOperationException("WebSocket endpoint URI is missing.");
                }

                return WebSocketTextConnection.Connect(endpoint.Uri, cancellationToken);
            default:
                throw new InvalidOperationException("Unsupported remote endpoint protocol: " + endpoint.Protocol + ".");
        }
    }

    private static void SendHandshake(IRemoteConnection connection, RemoteEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.Protocol == RemoteEndpointProtocol.Tcp)
        {
            connection.SendMessage("AXSG-HOTRELOAD/1 HELLO", cancellationToken);
        }

        var helloPayload = JsonSerializer.Serialize(new
        {
            messageType = "hello",
            protocol = "axsg-hotreload",
            version = 1,
            runtime = Environment.Version.ToString(),
            pid = Environment.ProcessId
        });
        connection.SendMessage(helloPayload, cancellationToken);
    }

    private static bool TryParseApplyRequest(string message, out SourceGenHotReloadRemoteUpdateRequest request)
    {
        request = new SourceGenHotReloadRemoteUpdateRequest(0, null, null, false, Array.Empty<string>(), Array.Empty<string>(), null);

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var messageType = TryGetString(root, "messageType");
            if (!string.IsNullOrWhiteSpace(messageType) &&
                !string.Equals(messageType, "apply", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryGetInt64(root, "operationId", out var operationId) || operationId <= 0)
            {
                return false;
            }

            request = new SourceGenHotReloadRemoteUpdateRequest(
                OperationId: operationId,
                RequestId: TryGetString(root, "requestId"),
                CorrelationId: TryGetNullableInt64(root, "correlationId"),
                ApplyAll: TryGetBoolean(root, "applyAll"),
                TypeNames: ReadStringArray(root, "typeNames", "types"),
                BuildUris: ReadStringArray(root, "buildUris", "uris"),
                Trigger: TryGetString(root, "trigger"));

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveEndpoint(out RemoteEndpoint endpoint, out string error)
    {
        var raw = GetConfiguredEndpointRawValue();

        if (string.IsNullOrWhiteSpace(raw))
        {
            endpoint = default;
            error = "Remote endpoint is not configured. Set AXSG_HOTRELOAD_REMOTE_ENDPOINT.";
            return false;
        }

        if (TryParseUriEndpoint(raw, out endpoint) ||
            TryParseHostPortEndpoint(raw, out endpoint))
        {
            error = string.Empty;
            return true;
        }

        error = "Invalid AXSG_HOTRELOAD_REMOTE_ENDPOINT value '" + raw + "'. Expected host:port, tcp://host:port, ws://host:port/path, or wss://host:port/path.";
        endpoint = default;
        return false;
    }

    private static string GetConfiguredEndpointRawValue()
    {
        var explicitFlag = Environment.GetEnvironmentVariable(RemoteEndpointExplicitEnvVarName);
        if (string.Equals(explicitFlag, "false", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var raw = Environment.GetEnvironmentVariable(RemoteEndpointEnvVarName);
        if (string.Equals(raw, NoEndpointSentinel, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return raw ?? string.Empty;
    }

    private static bool TryParseUriEndpoint(string raw, out RemoteEndpoint endpoint)
    {
        endpoint = default;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        RemoteEndpointProtocol protocol;
        switch (uri.Scheme)
        {
            case "tcp":
                protocol = RemoteEndpointProtocol.Tcp;
                break;
            case "ws":
            case "wss":
                protocol = RemoteEndpointProtocol.WebSocket;
                break;
            default:
                return false;
        }

        var parsedPort = uri.Port;
        if (parsedPort <= 0)
        {
            return false;
        }

        endpoint = new RemoteEndpoint(
            protocol,
            uri.Host,
            parsedPort,
            protocol == RemoteEndpointProtocol.WebSocket ? uri : null);
        return true;
    }

    private static bool TryParseHostPortEndpoint(string raw, out RemoteEndpoint endpoint)
    {
        endpoint = default;

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        string host;
        int port;
        if (trimmed[0] == '[')
        {
            var closingIndex = trimmed.IndexOf(']');
            if (closingIndex <= 1 || closingIndex + 2 >= trimmed.Length || trimmed[closingIndex + 1] != ':')
            {
                return false;
            }

            host = trimmed.Substring(1, closingIndex - 1);
            var portText = trimmed.Substring(closingIndex + 2);
            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
                port <= 0)
            {
                return false;
            }
        }
        else
        {
            var lastSeparator = trimmed.LastIndexOf(':');
            if (lastSeparator <= 0 || lastSeparator == trimmed.Length - 1)
            {
                return false;
            }

            host = trimmed.Substring(0, lastSeparator);
            var portPart = trimmed.Substring(lastSeparator + 1);
            if (string.IsNullOrWhiteSpace(host) ||
                !int.TryParse(portPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
                port <= 0)
            {
                return false;
            }
        }

        endpoint = new RemoteEndpoint(RemoteEndpointProtocol.Tcp, host, port, Uri: null);
        return true;
    }

    private static bool TryGetInt64(JsonElement root, string propertyName, out long value)
    {
        value = default;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt64(out value);
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static long? TryGetNullableInt64(JsonElement root, string propertyName)
    {
        return TryGetInt64(root, propertyName, out var value) ? value : null;
    }

    private static bool TryGetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String &&
            bool.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return false;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return element.GetString();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string primaryName, string fallbackName)
    {
        if (TryReadStringArray(root, primaryName, out var values))
        {
            return values;
        }

        if (TryReadStringArray(root, fallbackName, out values))
        {
            return values;
        }

        return Array.Empty<string>();
    }

    private static bool TryReadStringArray(JsonElement root, string propertyName, out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            list.Add(value);
        }

        values = list;
        return true;
    }

    private readonly record struct RemoteEndpoint(
        RemoteEndpointProtocol Protocol,
        string Host,
        int Port,
        Uri? Uri)
    {
        public string DisplayText => Protocol == RemoteEndpointProtocol.WebSocket && Uri is not null
            ? Uri.ToString()
            : Host + ":" + Port.ToString(CultureInfo.InvariantCulture);
    }

    private enum RemoteEndpointProtocol
    {
        Tcp = 0,
        WebSocket = 1
    }

    private interface IRemoteConnection : IDisposable
    {
        void SendMessage(string payload, CancellationToken cancellationToken);

        bool TryReceiveMessage(out string message, CancellationToken cancellationToken);
    }

    private sealed class TcpLineConnection : IRemoteConnection
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private TcpLineConnection(TcpClient client)
        {
            _client = client;
            var stream = client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
        }

        public static TcpLineConnection Connect(string host, int port, CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            client.ConnectAsync(host, port, cancellationToken).GetAwaiter().GetResult();
            return new TcpLineConnection(client);
        }

        public void SendMessage(string payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _writer.WriteLine(payload);
        }

        public bool TryReceiveMessage(out string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = _reader.ReadLine();
            if (line is null)
            {
                message = string.Empty;
                return false;
            }

            message = line;
            return true;
        }

        public void Dispose()
        {
            try
            {
                _writer.Dispose();
            }
            catch
            {
                // Best effort.
            }

            try
            {
                _reader.Dispose();
            }
            catch
            {
                // Best effort.
            }

            try
            {
                _client.Dispose();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private sealed class WebSocketTextConnection : IRemoteConnection
    {
        private readonly ClientWebSocket _client;

        private WebSocketTextConnection(ClientWebSocket client)
        {
            _client = client;
        }

        public static WebSocketTextConnection Connect(Uri uri, CancellationToken cancellationToken)
        {
            var client = new ClientWebSocket();
            client.ConnectAsync(uri, cancellationToken).GetAwaiter().GetResult();
            return new WebSocketTextConnection(client);
        }

        public void SendMessage(string payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = Encoding.UTF8.GetBytes(payload);
            _client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).GetAwaiter().GetResult();
        }

        public bool TryReceiveMessage(out string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            message = string.Empty;

            if (_client.State != WebSocketState.Open && _client.State != WebSocketState.CloseReceived)
            {
                return false;
            }

            var buffer = new byte[4096];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = _client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).GetAwaiter().GetResult();
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return false;
                }

                if (result.Count > 0)
                {
                    stream.Write(buffer, 0, result.Count);
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            message = Encoding.UTF8.GetString(stream.ToArray());
            return message.Length > 0;
        }

        public void Dispose()
        {
            try
            {
                if (_client.State == WebSocketState.Open || _client.State == WebSocketState.CloseReceived)
                {
                    _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch
            {
                // Best effort close.
            }

            _client.Dispose();
        }
    }
}
