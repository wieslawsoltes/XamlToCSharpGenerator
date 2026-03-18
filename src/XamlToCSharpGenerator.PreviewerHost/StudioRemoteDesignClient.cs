using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.PreviewerHost;

internal sealed class StudioRemoteDesignClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StudioRemoteDesignClient(TcpClient client)
    {
        _client = client;
        NetworkStream stream = client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    public static async Task<StudioRemoteDesignClient> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadlineUtc = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                return new StudioRemoteDesignClient(client);
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException or InvalidOperationException)
            {
                client.Dispose();
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                lastError = ex;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            "Timed out connecting to the preview design server." +
            (lastError is null ? string.Empty : " " + lastError.Message),
            lastError);
    }

    public async Task<JsonElement> SendCommandAsync(
        string command,
        object? payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);

        string requestId = Guid.NewGuid().ToString("N");
        string requestJson = JsonSerializer.Serialize(
            new
            {
                command,
                requestId,
                payload
            },
            JsonRpcSerializer.DefaultOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            string? responseLine = await _reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new InvalidOperationException("Preview design server returned an empty response.");
            }

            using JsonDocument responseDocument = JsonDocument.Parse(responseLine);
            JsonElement root = responseDocument.RootElement;
            bool ok = root.TryGetProperty("ok", out JsonElement okElement) &&
                      okElement.ValueKind == JsonValueKind.True;
            string? error = root.TryGetProperty("error", out JsonElement errorElement) &&
                            errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            if (!ok)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "Preview design command failed."
                    : error);
            }

            return root.TryGetProperty("payload", out JsonElement payloadElement)
                ? payloadElement.Clone()
                : default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        _reader.Dispose();
        _writer.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
