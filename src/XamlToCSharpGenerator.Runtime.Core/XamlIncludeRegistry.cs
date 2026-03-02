using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlIncludeRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenIncludeDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(SourceGenIncludeDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Kind))
        {
            throw new ArgumentException("Include kind must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Source))
        {
            throw new ArgumentException("Include source must be provided.", nameof(descriptor));
        }

        var bag = Entries.GetOrAdd(descriptor.Uri, static _ => new ConcurrentBag<SourceGenIncludeDescriptor>());
        bag.Add(descriptor);
    }

    public static bool TryRegisterSerialized(string payload)
    {
        if (!SourceGenRegistryPayloadSerialization.TryDeserializeInclude(payload, out var descriptor) ||
            descriptor is null)
        {
            return false;
        }

        Register(descriptor);
        return true;
    }

    public static string Serialize(SourceGenIncludeDescriptor descriptor)
    {
        return SourceGenRegistryPayloadSerialization.Serialize(descriptor);
    }

    public static void Register(
        string uri,
        string kind,
        string source,
        string mergeTarget,
        bool isAbsoluteUri,
        string rawXaml)
    {
        Register(new SourceGenIncludeDescriptor(uri, kind, source, mergeTarget, isAbsoluteUri, rawXaml));
    }

    public static IReadOnlyCollection<SourceGenIncludeDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var includes))
        {
            return Array.Empty<SourceGenIncludeDescriptor>();
        }

        return includes.ToArray();
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
