using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;

namespace XamlToCSharpGenerator.PreviewerHost.Protocol;

internal sealed class AvaloniaDesignerTransport : IAsyncDisposable
{
    internal const double DefaultViewportWidth = 1280;
    internal const double DefaultViewportHeight = 800;
    internal const double DefaultDpi = 96;
    internal const double DefaultViewportScale = 1;

    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public AvaloniaDesignerTransport(NetworkStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task SendUpdateXamlAsync(
        string xamlText,
        string assemblyPath,
        string xamlFileProjectPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(xamlText);
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        ArgumentException.ThrowIfNullOrEmpty(xamlFileProjectPath);

        var document = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Xaml"] = xamlText,
            ["AssemblyPath"] = assemblyPath,
            ["XamlFileProjectPath"] = xamlFileProjectPath
        };

        await SendMessageAsync(
            AvaloniaDesignerMessageGuids.UpdateXaml,
            MinimalBson.SerializeDocument(document),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SendInitialClientBootstrapAsync(
        double? viewportWidth,
        double? viewportHeight,
        double? viewportScale,
        CancellationToken cancellationToken)
    {
        var dpi = NormalizeViewportDpi(viewportScale);
        var renderInfo = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["DpiX"] = dpi,
            ["DpiY"] = dpi
        };
        await SendMessageAsync(
            AvaloniaDesignerMessageGuids.ClientRenderInfo,
            MinimalBson.SerializeDocument(renderInfo),
            cancellationToken).ConfigureAwait(false);

        var viewportAllocated = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Width"] = NormalizeViewportSize(viewportWidth, DefaultViewportWidth),
            ["Height"] = NormalizeViewportSize(viewportHeight, DefaultViewportHeight),
            ["DpiX"] = dpi,
            ["DpiY"] = dpi
        };
        await SendMessageAsync(
            AvaloniaDesignerMessageGuids.ClientViewportAllocated,
            MinimalBson.SerializeDocument(viewportAllocated),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<object?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var header = new byte[20];
        if (!await ReadExactAsync(header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        if (payloadLength < 0)
        {
            throw new InvalidDataException("Avalonia designer transport payload length is invalid.");
        }

        var payload = new byte[payloadLength];
        if (payloadLength > 0 && !await ReadExactAsync(payload, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Avalonia designer transport payload was truncated.");
        }

        var messageType = new Guid(header.AsSpan(4, 16));
        var document = MinimalBson.DeserializeDocument(payload);
        return ParseMessage(messageType, document);
    }

    public async ValueTask DisposeAsync()
    {
        _sendGate.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    private static object ParseMessage(Guid messageType, IReadOnlyDictionary<string, object?> document)
    {
        if (messageType == AvaloniaDesignerMessageGuids.StartDesignerSession)
        {
            return new StartDesignerSessionPayload(GetString(document, "SessionId") ?? string.Empty);
        }

        if (messageType == AvaloniaDesignerMessageGuids.HtmlTransportStarted)
        {
            return new HtmlTransportStartedPayload(GetString(document, "Uri") ?? string.Empty);
        }

        if (messageType == AvaloniaDesignerMessageGuids.UpdateXamlResult)
        {
            return new UpdateXamlResultPayload(
                GetString(document, "Error"),
                GetString(document, "Handle"),
                TryGetDocument(document, "Exception", out var exceptionDocument)
                    ? new PreviewExceptionDetails(
                        GetString(exceptionDocument, "ExceptionType"),
                        GetString(exceptionDocument, "Message"),
                        GetNullableInt32(exceptionDocument, "LineNumber"),
                        GetNullableInt32(exceptionDocument, "LinePosition"))
                    : null);
        }

        return new UnknownDesignerMessage(messageType, document);
    }

    private async Task SendMessageAsync(Guid messageType, byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = new byte[20];
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), payload.Length);
            messageType.TryWriteBytes(header.AsSpan(4, 16));

            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (payload.Length > 0)
            {
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await _stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return offset == 0;
            }

            offset += bytesRead;
        }

        return true;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> document, string name)
    {
        return document.TryGetValue(name, out var value) && value is string stringValue
            ? stringValue
            : null;
    }

    private static int? GetNullableInt32(IReadOnlyDictionary<string, object?> document, string name)
    {
        if (!document.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            _ => null
        };
    }

    private static bool TryGetDocument(
        IReadOnlyDictionary<string, object?> document,
        string name,
        out IReadOnlyDictionary<string, object?> value)
    {
        if (document.TryGetValue(name, out var rawValue) &&
            rawValue is IReadOnlyDictionary<string, object?> typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default!;
        return false;
    }

    private static double NormalizeViewportSize(double? viewportSize, double fallbackValue)
    {
        return viewportSize is > 0
            ? viewportSize.Value
            : fallbackValue;
    }

    private static double NormalizeViewportDpi(double? viewportScale)
    {
        var normalizedScale = viewportScale is > 0
            ? viewportScale.Value
            : DefaultViewportScale;
        return DefaultDpi * normalizedScale;
    }
}
