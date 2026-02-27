using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotReloadTransportStatus
{
    public SourceGenHotReloadTransportStatus(
        SourceGenHotReloadTransportStatusKind kind,
        string transportName,
        SourceGenHotReloadTransportMode mode,
        string message,
        DateTimeOffset timestampUtc,
        bool isFallback,
        Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(transportName))
        {
            throw new ArgumentException("Transport name must not be null or empty.", nameof(transportName));
        }

        Kind = kind;
        TransportName = transportName;
        Mode = mode;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        TimestampUtc = timestampUtc;
        IsFallback = isFallback;
        Exception = exception;
    }

    public SourceGenHotReloadTransportStatusKind Kind { get; }

    public string TransportName { get; }

    public SourceGenHotReloadTransportMode Mode { get; }

    public string Message { get; }

    public DateTimeOffset TimestampUtc { get; }

    public bool IsFallback { get; }

    public Exception? Exception { get; }
}
