using System;
using System.Threading;

namespace XamlToCSharpGenerator.Runtime;

public interface ISourceGenHotReloadTransport
{
    string Name { get; }

    SourceGenHotReloadTransportCapabilities Capabilities { get; }

    SourceGenHotReloadHandshakeResult StartHandshake(TimeSpan timeout, CancellationToken cancellationToken = default);

    void Stop();
}
