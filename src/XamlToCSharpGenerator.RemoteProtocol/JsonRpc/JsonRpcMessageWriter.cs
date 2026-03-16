using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

public class JsonRpcMessageWriter
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonRpcMessageWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task WriteAsync<T>(T payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonRpcSerializer.DefaultOptions);
        var header = Encoding.ASCII.GetBytes("Content-Length: " + bytes.Length + "\r\n\r\n");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, CancellationToken.None).ConfigureAwait(false);
            await _stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
            await _stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
