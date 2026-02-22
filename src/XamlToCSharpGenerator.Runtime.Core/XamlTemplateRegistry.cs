using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlTemplateRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenTemplateDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string uri, string kind, string? key, string? targetTypeName, string? dataType, string rawXaml)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Template kind must be provided.", nameof(kind));
        }

        var bag = Entries.GetOrAdd(uri, static _ => new ConcurrentBag<SourceGenTemplateDescriptor>());
        bag.Add(new SourceGenTemplateDescriptor(uri, kind, key, targetTypeName, dataType, rawXaml));
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
