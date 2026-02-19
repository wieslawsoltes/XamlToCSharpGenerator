using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlCompiledBindingRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenCompiledBindingDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(
        string uri,
        string targetTypeName,
        string targetPropertyName,
        string path,
        string sourceTypeName,
        Func<object, object?> accessor)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(targetTypeName))
        {
            throw new ArgumentException("Target type name must be provided.", nameof(targetTypeName));
        }

        if (string.IsNullOrWhiteSpace(targetPropertyName))
        {
            throw new ArgumentException("Target property name must be provided.", nameof(targetPropertyName));
        }

        if (string.IsNullOrWhiteSpace(sourceTypeName))
        {
            throw new ArgumentException("Source type name must be provided.", nameof(sourceTypeName));
        }

        if (accessor is null)
        {
            throw new ArgumentNullException(nameof(accessor));
        }

        var bag = Entries.GetOrAdd(uri, static _ => new ConcurrentBag<SourceGenCompiledBindingDescriptor>());
        bag.Add(new SourceGenCompiledBindingDescriptor(
            uri,
            targetTypeName,
            targetPropertyName,
            path,
            sourceTypeName,
            accessor));
    }

    public static IReadOnlyCollection<SourceGenCompiledBindingDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var items))
        {
            return Array.Empty<SourceGenCompiledBindingDescriptor>();
        }

        return items.ToArray();
    }

    public static bool TryEvaluate(
        string uri,
        string targetTypeName,
        string targetPropertyName,
        object source,
        out object? value)
    {
        value = null;
        if (source is null)
        {
            return false;
        }

        if (!Entries.TryGetValue(uri, out var items))
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item.TargetTypeName.Equals(targetTypeName, StringComparison.Ordinal) &&
                item.TargetPropertyName.Equals(targetPropertyName, StringComparison.Ordinal))
            {
                value = item.Accessor(source);
                return true;
            }
        }

        return false;
    }
}
