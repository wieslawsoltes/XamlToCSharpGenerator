using System;
using System.Collections.Concurrent;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenArtifactRefreshRegistry
{
    private static readonly ConcurrentDictionary<Type, Action> RefreshActionsByType = new();

    public static void Register(Type type, Action refreshAction)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(refreshAction);

        var normalizedType = NormalizeType(type);
        RefreshActionsByType[normalizedType] = refreshAction;
    }

    public static bool TryRefresh(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var visited = new System.Collections.Generic.HashSet<Type>();
        var current = NormalizeType(type);
        while (visited.Add(current))
        {
            if (RefreshActionsByType.TryGetValue(current, out var refreshAction))
            {
                refreshAction();
                return true;
            }

            if (current.DeclaringType is not null)
            {
                current = NormalizeType(current.DeclaringType);
                continue;
            }

            if (current.BaseType is null)
            {
                break;
            }

            current = NormalizeType(current.BaseType);
        }

        return false;
    }

    public static void Clear()
    {
        RefreshActionsByType.Clear();
    }

    private static Type NormalizeType(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }
}
