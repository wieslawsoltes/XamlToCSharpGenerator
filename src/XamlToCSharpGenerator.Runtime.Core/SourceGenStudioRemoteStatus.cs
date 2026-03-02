using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenStudioRemoteStatus(
    bool IsEnabled,
    bool IsListening,
    string Host,
    int Port,
    int ActiveClientCount,
    string? LastError,
    string? VncEndpoint,
    DateTimeOffset UpdatedAtUtc)
{
    public static SourceGenStudioRemoteStatus Disabled(string? vncEndpoint = null)
    {
        return new SourceGenStudioRemoteStatus(
            IsEnabled: false,
            IsListening: false,
            Host: "0.0.0.0",
            Port: 0,
            ActiveClientCount: 0,
            LastError: null,
            VncEndpoint: vncEndpoint,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }
}
