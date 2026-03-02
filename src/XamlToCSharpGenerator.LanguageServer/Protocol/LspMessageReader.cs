using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;

namespace XamlToCSharpGenerator.LanguageServer.Protocol;

internal sealed class LspMessageReader
{
    private readonly Stream _stream;

    public LspMessageReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var contentLength = await ReadContentLengthAsync(cancellationToken).ConfigureAwait(false);
        if (contentLength <= 0)
        {
            return null;
        }

        var rented = ArrayPool<byte>.Shared.Rent(contentLength);
        try
        {
            var read = 0;
            while (read < contentLength)
            {
                var chunk = await _stream.ReadAsync(
                    rented.AsMemory(read, contentLength - read),
                    cancellationToken).ConfigureAwait(false);
                if (chunk == 0)
                {
                    return null;
                }

                read += chunk;
            }

            return JsonDocument.Parse(rented.AsMemory(0, contentLength));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async Task<int> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(256);

        while (true)
        {
            var readBuffer = new byte[1];
            var read = await _stream.ReadAsync(readBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return -1;
            }

            var value = readBuffer[0];
            headerBytes.Add(value);

            if (headerBytes.Count >= 4 &&
                headerBytes[^4] == '\r' &&
                headerBytes[^3] == '\n' &&
                headerBytes[^2] == '\r' &&
                headerBytes[^1] == '\n')
            {
                break;
            }
        }

        var headers = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = line.Substring("Content-Length:".Length).Trim();
            if (int.TryParse(text, out var length))
            {
                return length;
            }
        }

        return -1;
    }
}
