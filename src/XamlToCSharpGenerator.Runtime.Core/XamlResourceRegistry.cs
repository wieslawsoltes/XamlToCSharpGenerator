using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlResourceRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SourceGenResourceDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(SourceGenResourceDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Key))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(descriptor));
        }

        var byKey = Entries.GetOrAdd(
            descriptor.Uri,
            static _ => new ConcurrentDictionary<string, SourceGenResourceDescriptor>(StringComparer.Ordinal));
        byKey[descriptor.Key] = descriptor;
    }

    public static bool TryRegisterSerialized(string payload)
    {
        if (!SourceGenRegistryPayloadSerialization.TryDeserializeResource(payload, out var descriptor) ||
            descriptor is null)
        {
            return false;
        }

        Register(descriptor);
        return true;
    }

    public static string Serialize(SourceGenResourceDescriptor descriptor)
    {
        return SourceGenRegistryPayloadSerialization.Serialize(descriptor);
    }

    public static void Register(string uri, string key, string typeName, string rawXaml)
    {
        Register(new SourceGenResourceDescriptor(uri, key, typeName, rawXaml));
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

    public static bool ContainsKey(string uri, string key)
    {
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return Entries.TryGetValue(uri, out var byKey) && byKey.ContainsKey(key);
    }

    public static bool HasEntries(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        return Entries.TryGetValue(uri, out var byKey) && !byKey.IsEmpty;
    }

    public static IReadOnlyCollection<SourceGenResourceDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var byKey))
        {
            return Array.Empty<SourceGenResourceDescriptor>();
        }

        return byKey.Values.ToArray();
    }

    public static void Clear(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Entries.TryRemove(uri, out _);
    }

    public static void Clear()
    {
        Entries.Clear();
    }
}
