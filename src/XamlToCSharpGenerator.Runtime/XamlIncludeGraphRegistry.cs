using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlIncludeGraphRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SourceGenIncludeEdgeDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);
    private static int Sequence;

    public static void Register(string sourceUri, string includedUri, string mergeTarget)
    {
        if (string.IsNullOrWhiteSpace(sourceUri))
        {
            throw new ArgumentException("Source URI must be provided.", nameof(sourceUri));
        }

        if (string.IsNullOrWhiteSpace(includedUri))
        {
            throw new ArgumentException("Included URI must be provided.", nameof(includedUri));
        }

        var normalizedMergeTarget = string.IsNullOrWhiteSpace(mergeTarget) ? "Unknown" : mergeTarget;
        var map = Entries.GetOrAdd(
            sourceUri,
            static _ => new ConcurrentDictionary<string, SourceGenIncludeEdgeDescriptor>(StringComparer.OrdinalIgnoreCase));
        var order = Interlocked.Increment(ref Sequence);
        map[includedUri + "|" + normalizedMergeTarget] =
            new SourceGenIncludeEdgeDescriptor(sourceUri, includedUri, normalizedMergeTarget, order);
    }

    public static void Clear()
    {
        Entries.Clear();
        Interlocked.Exchange(ref Sequence, 0);
    }

    public static void Clear(string sourceUri)
    {
        if (string.IsNullOrWhiteSpace(sourceUri))
        {
            return;
        }

        Entries.TryRemove(sourceUri, out _);
    }

    public static IReadOnlyList<SourceGenIncludeEdgeDescriptor> GetDirect(string sourceUri, string? mergeTarget = null)
    {
        if (!Entries.TryGetValue(sourceUri, out var includes))
        {
            return Array.Empty<SourceGenIncludeEdgeDescriptor>();
        }

        IEnumerable<SourceGenIncludeEdgeDescriptor> result = includes.Values;
        if (!string.IsNullOrWhiteSpace(mergeTarget))
        {
            result = result.Where(edge => edge.MergeTarget.Equals(mergeTarget, StringComparison.OrdinalIgnoreCase));
        }

        return result
            .OrderBy(static edge => edge.Order)
            .ToArray();
    }

    public static IReadOnlyList<SourceGenIncludeEdgeDescriptor> GetTransitive(string sourceUri, string? mergeTarget = null)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SourceGenIncludeEdgeDescriptor>();
        visited.Add(sourceUri);

        Visit(sourceUri, mergeTarget, visited, result);
        return result;
    }

    private static void Visit(
        string sourceUri,
        string? mergeTarget,
        HashSet<string> visited,
        List<SourceGenIncludeEdgeDescriptor> result)
    {
        foreach (var edge in GetDirect(sourceUri, mergeTarget))
        {
            if (!visited.Add(edge.IncludedUri))
            {
                continue;
            }

            result.Add(edge);
            Visit(edge.IncludedUri, mergeTarget, visited, result);
        }
    }
}
