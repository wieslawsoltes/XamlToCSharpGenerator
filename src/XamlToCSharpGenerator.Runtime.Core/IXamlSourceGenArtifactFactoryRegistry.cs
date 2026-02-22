using System;

namespace XamlToCSharpGenerator.Runtime;

public interface IXamlSourceGenArtifactFactoryRegistry
{
    event Action<string>? DuplicateUriRegistration;

    event Action<string>? MissingUriRequested;

    void Register(string uri, Func<IServiceProvider?, object> factory);

    void Unregister(string uri);

    bool TryCreate(IServiceProvider? serviceProvider, string uri, out object? value);

    void Clear();
}
