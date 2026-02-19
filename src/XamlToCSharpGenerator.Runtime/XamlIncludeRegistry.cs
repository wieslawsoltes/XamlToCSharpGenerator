using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlIncludeRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenIncludeDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(
        string uri,
        string kind,
        string source,
        string mergeTarget,
        bool isAbsoluteUri,
        string rawXaml)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Include kind must be provided.", nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Include source must be provided.", nameof(source));
        }

        var bag = Entries.GetOrAdd(uri, static _ => new ConcurrentBag<SourceGenIncludeDescriptor>());
        bag.Add(new SourceGenIncludeDescriptor(uri, kind, source, mergeTarget, isAbsoluteUri, rawXaml));
    }

    public static IReadOnlyCollection<SourceGenIncludeDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var includes))
        {
            return Array.Empty<SourceGenIncludeDescriptor>();
        }

        return includes.ToArray();
    }
}
