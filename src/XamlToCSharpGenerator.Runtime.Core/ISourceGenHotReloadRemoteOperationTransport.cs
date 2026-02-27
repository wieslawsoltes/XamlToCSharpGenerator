using System;

namespace XamlToCSharpGenerator.Runtime;

public interface ISourceGenHotReloadRemoteOperationTransport
{
    event Action<SourceGenHotReloadRemoteUpdateRequest>? RemoteUpdateReceived;

    void PublishRemoteUpdateResult(SourceGenHotReloadRemoteUpdateResult result);
}
