using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceInfoRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenSourceInfoDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(
        string uri,
        string kind,
        string name,
        string filePath,
        int line,
        int column)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Kind must be provided.", nameof(kind));
        }

        var bag = Entries.GetOrAdd(uri, static _ => new ConcurrentBag<SourceGenSourceInfoDescriptor>());
        bag.Add(new SourceGenSourceInfoDescriptor(
            uri,
            kind,
            name ?? string.Empty,
            filePath ?? string.Empty,
            Math.Max(1, line),
            Math.Max(1, column)));
    }

    public static IReadOnlyCollection<SourceGenSourceInfoDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var items))
        {
            return Array.Empty<SourceGenSourceInfoDescriptor>();
        }

        return OrderDescriptors(items);
    }

    public static IReadOnlyCollection<SourceGenSourceInfoDescriptor> GetByKind(string uri, string kind)
    {
        if (!Entries.TryGetValue(uri, out var items) || string.IsNullOrWhiteSpace(kind))
        {
            return Array.Empty<SourceGenSourceInfoDescriptor>();
        }

        return OrderDescriptors(items.Where(descriptor =>
            descriptor.Kind.Equals(kind, StringComparison.Ordinal)));
    }

    public static bool TryGet(string uri, string kind, string name, out SourceGenSourceInfoDescriptor? descriptor)
    {
        descriptor = null;
        if (!Entries.TryGetValue(uri, out var items) ||
            string.IsNullOrWhiteSpace(kind) ||
            string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var candidate in OrderDescriptors(items))
        {
            if (!candidate.Kind.Equals(kind, StringComparison.Ordinal) ||
                !candidate.Name.Equals(name, StringComparison.Ordinal))
            {
                continue;
            }

            descriptor = candidate;
            return true;
        }

        return false;
    }

    public static void Clear()
    {
        Entries.Clear();
    }

    public static void Clear(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Entries.TryRemove(uri, out _);
    }

    private static SourceGenSourceInfoDescriptor[] OrderDescriptors(IEnumerable<SourceGenSourceInfoDescriptor> items)
    {
        return items
            .OrderBy(descriptor => descriptor.Kind, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Line)
            .ThenBy(descriptor => descriptor.Column)
            .ToArray();
    }
}
