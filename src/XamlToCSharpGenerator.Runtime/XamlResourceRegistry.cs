using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlResourceRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SourceGenResourceDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string uri, string key, string typeName, string rawXaml)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(key));
        }

        var byKey = Entries.GetOrAdd(uri, static _ => new ConcurrentDictionary<string, SourceGenResourceDescriptor>(StringComparer.Ordinal));
        byKey[key] = new SourceGenResourceDescriptor(uri, key, typeName, rawXaml);
    }

    public static bool TryGet(string uri, string key, out SourceGenResourceDescriptor? descriptor)
    {
        descriptor = null;
        if (!Entries.TryGetValue(uri, out var byKey))
        {
            return false;
        }

        return byKey.TryGetValue(key, out descriptor);
    }

    public static IReadOnlyCollection<SourceGenResourceDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var byKey))
        {
            return Array.Empty<SourceGenResourceDescriptor>();
        }

        return byKey.Values.ToArray();
    }
}
