using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace XamlToCSharpGenerator.LanguageServer.Protocol;

internal sealed class LspMessageWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public LspMessageWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task WriteAsync<T>(T payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        var header = Encoding.ASCII.GetBytes("Content-Length: " + bytes.Length + "\r\n\r\n");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
