using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenTypeUriRegistry
{
    private static readonly IXamlSourceGenUriMapper UriMapper = XamlSourceGenUriMapper.Default;
    private static readonly ConcurrentDictionary<Type, string> UrisByType = new();

    public static void Register(Type type, string uri)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        var normalizedType = NormalizeType(type);
        var normalizedUri = UriMapper.Normalize(uri);
        UrisByType[normalizedType] = normalizedUri;
    }

    public static bool TryGetUri(Type type, out string uri)
    {
        ArgumentNullException.ThrowIfNull(type);
        var visited = new HashSet<Type>();
        var current = NormalizeType(type);

        while (current is not null && visited.Add(current))
        {
            if (UrisByType.TryGetValue(current, out uri!))
            {
                return true;
            }

            if (current.DeclaringType is not null)
            {
                current = NormalizeType(current.DeclaringType);
                continue;
            }

            current = current.BaseType is null ? null : NormalizeType(current.BaseType);
        }

        uri = string.Empty;
        return false;
    }

    public static void Clear()
    {
        UrisByType.Clear();
    }

    private static Type NormalizeType(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }
}
