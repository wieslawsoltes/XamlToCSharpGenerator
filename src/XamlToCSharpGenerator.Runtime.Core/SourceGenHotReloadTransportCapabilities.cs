using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotReloadTransportCapabilities
{
    public SourceGenHotReloadTransportCapabilities(
        bool isSupported,
        bool supportsMetadataUpdates,
        bool supportsRemoteConnection,
        bool requiresEndpointConfiguration,
        string diagnostic)
    {
        IsSupported = isSupported;
        SupportsMetadataUpdates = supportsMetadataUpdates;
        SupportsRemoteConnection = supportsRemoteConnection;
        RequiresEndpointConfiguration = requiresEndpointConfiguration;
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public bool IsSupported { get; }

    public bool SupportsMetadataUpdates { get; }

    public bool SupportsRemoteConnection { get; }

    public bool RequiresEndpointConfiguration { get; }

    public string Diagnostic { get; }
}
