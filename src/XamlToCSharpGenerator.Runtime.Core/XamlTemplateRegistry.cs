using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlTemplateRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenTemplateDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(SourceGenTemplateDescriptor descriptor)
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
            throw new ArgumentException("Template kind must be provided.", nameof(descriptor));
        }

        var bag = Entries.GetOrAdd(descriptor.Uri, static _ => new ConcurrentBag<SourceGenTemplateDescriptor>());
        bag.Add(descriptor);
    }

    public static bool TryRegisterSerialized(string payload)
    {
        if (!SourceGenRegistryPayloadSerialization.TryDeserializeTemplate(payload, out var descriptor) ||
            descriptor is null)
        {
            return false;
        }

        Register(descriptor);
        return true;
    }

    public static string Serialize(SourceGenTemplateDescriptor descriptor)
    {
        return SourceGenRegistryPayloadSerialization.Serialize(descriptor);
    }

    public static void Register(string uri, string kind, string? key, string? targetTypeName, string? dataType, string rawXaml)
    {
        Register(new SourceGenTemplateDescriptor(uri, kind, key, targetTypeName, dataType, rawXaml));
    }

    public static IReadOnlyCollection<SourceGenTemplateDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var items))
        {
            return Array.Empty<SourceGenTemplateDescriptor>();
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
