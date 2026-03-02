using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlStyleRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenStyleDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(SourceGenStyleDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Selector))
        {
            // Selector-less style blocks are valid as local resource containers but
            // are not targetable through the style selector registry.
            return;
        }

        var bag = Entries.GetOrAdd(descriptor.Uri, static _ => new ConcurrentBag<SourceGenStyleDescriptor>());
        bag.Add(descriptor);
    }

    public static bool TryRegisterSerialized(string payload)
    {
        if (!SourceGenRegistryPayloadSerialization.TryDeserializeStyle(payload, out var descriptor) ||
            descriptor is null)
        {
            return false;
        }

        Register(descriptor);
        return true;
    }

    public static string Serialize(SourceGenStyleDescriptor descriptor)
    {
        return SourceGenRegistryPayloadSerialization.Serialize(descriptor);
    }

    public static void Register(string uri, string? key, string selector, string? targetTypeName, string rawXaml)
    {
        Register(new SourceGenStyleDescriptor(uri, key, selector, targetTypeName, rawXaml));
    }

    public static IReadOnlyCollection<SourceGenStyleDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var items))
        {
            return Array.Empty<SourceGenStyleDescriptor>();
        }

        return items.ToArray();
    }

    public static void Clear(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Entries.TryRemove(uri, out _);
    }
}
