using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlCompiledBindingRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<SourceGenCompiledBindingDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(SourceGenCompiledBindingDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.TargetTypeName))
        {
            throw new ArgumentException("Target type name must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.TargetPropertyName))
        {
            throw new ArgumentException("Target property name must be provided.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.SourceTypeName))
        {
            throw new ArgumentException("Source type name must be provided.", nameof(descriptor));
        }

        if (descriptor.Accessor is null)
        {
            throw new ArgumentException("Compiled binding accessor must be provided.", nameof(descriptor));
        }

        var bag = Entries.GetOrAdd(descriptor.Uri, static _ => new ConcurrentBag<SourceGenCompiledBindingDescriptor>());
        bag.Add(descriptor);
    }

    public static void Register(
        string uri,
        string targetTypeName,
        string targetPropertyName,
        string path,
        string sourceTypeName,
        Func<object, object?> accessor)
    {
        Register(new SourceGenCompiledBindingDescriptor(
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

    public static void Clear(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Entries.TryRemove(uri, out _);
    }
}
