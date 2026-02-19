using System;
using System.Collections.Concurrent;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenRegistry
{
    private static readonly ConcurrentDictionary<string, Func<IServiceProvider?, object>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static event Action<string>? DuplicateUriRegistration;

    public static event Action<string>? MissingUriRequested;

    public static void Register(string uri, Func<IServiceProvider?, object> factory)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        var providedFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (!Entries.TryAdd(uri, providedFactory))
        {
            DuplicateUriRegistration?.Invoke(uri);
        }
    }

    public static bool TryCreate(IServiceProvider? serviceProvider, string uri, out object? value)
    {
        if (Entries.TryGetValue(uri, out var factory))
        {
            value = factory(serviceProvider);
            return true;
        }

        value = null;
        MissingUriRequested?.Invoke(uri);
        return false;
    }

    public static void Clear()
    {
        Entries.Clear();
    }
}
