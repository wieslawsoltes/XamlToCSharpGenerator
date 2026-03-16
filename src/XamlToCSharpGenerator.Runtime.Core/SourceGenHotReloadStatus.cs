namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotReloadStatus(
    bool IsEnabled,
    bool IsIdePollingFallbackEnabled,
    int RegisteredTypeCount,
    int RegisteredBuildUriCount,
    SourceGenHotReloadTransportMode TransportMode,
    SourceGenHotReloadTransportStatus? LastTransportStatus,
    SourceGenHotReloadRemoteOperationStatus? LastRemoteOperationStatus);
