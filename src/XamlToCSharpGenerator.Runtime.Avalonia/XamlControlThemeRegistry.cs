using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using global::Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlControlThemeRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<SourceGenControlThemeDescriptor>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(SourceGenControlThemeDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(descriptor));
        }

        var queue = Entries.GetOrAdd(descriptor.Uri, static _ => new ConcurrentQueue<SourceGenControlThemeDescriptor>());
        queue.Enqueue(descriptor with
        {
            BasedOnKey = TryExtractBasedOnKey(descriptor.BasedOn),
            NormalizedThemeVariant = NormalizeThemeVariant(descriptor.ThemeVariant)
        });
    }

    public static void Register(
        string uri,
        string? key,
        string? targetTypeName,
        string? basedOn,
        string? themeVariant,
        string rawXaml)
    {
        Register(uri, key, targetTypeName, basedOn, themeVariant, rawXaml, factory: null);
    }

    public static void Register(
        string uri,
        string? key,
        string? targetTypeName,
        string? basedOn,
        string? themeVariant,
        string rawXaml,
        Func<ControlTheme>? factory)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI must be provided.", nameof(uri));
        }

        Register(new SourceGenControlThemeDescriptor(
            Uri: uri,
            Key: key,
            TargetTypeName: targetTypeName,
            BasedOn: basedOn,
            ThemeVariant: themeVariant,
            RawXaml: rawXaml,
            Factory: factory));
    }

    public static IReadOnlyCollection<SourceGenControlThemeDescriptor> GetAll(string uri)
    {
        if (!Entries.TryGetValue(uri, out var items))
        {
            return Array.Empty<SourceGenControlThemeDescriptor>();
        }

        return items.ToArray();
    }

    public static bool TryMaterialize(string uri, string key, out ControlTheme? theme)
    {
        theme = null;
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!Entries.TryGetValue(uri, out var queue))
        {
            return false;
        }

        var entries = queue.ToArray();
        if (entries.Length == 0)
        {
            return false;
        }

        var byKey = BuildKeyLookup(entries);
        var normalizedKey = key.Trim();
        if (!byKey.TryGetValue(normalizedKey, out var descriptor))
        {
            return false;
        }

        return TryMaterialize(
            descriptor,
            byKey,
            new HashSet<string>(StringComparer.Ordinal),
            out theme);
    }

    public static bool TryMaterialize(string uri, Type targetType, string? themeVariant, out ControlTheme? theme)
    {
        if (targetType is null)
        {
            throw new ArgumentNullException(nameof(targetType));
        }

        theme = null;
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!Entries.TryGetValue(uri, out var queue))
        {
            return false;
        }

        var entries = queue.ToArray();
        if (entries.Length == 0)
        {
            return false;
        }

        var requestedVariant = NormalizeThemeVariant(themeVariant);
        SourceGenControlThemeDescriptor? exactVariant = null;
        SourceGenControlThemeDescriptor? defaultVariant = null;

        for (var index = entries.Length - 1; index >= 0; index--)
        {
            var descriptor = entries[index];
            if (!MatchesTargetType(descriptor.TargetTypeName, targetType))
            {
                continue;
            }

            if (string.Equals(descriptor.NormalizedThemeVariant, requestedVariant, StringComparison.OrdinalIgnoreCase))
            {
                exactVariant = descriptor;
                break;
            }

            if (descriptor.NormalizedThemeVariant is null && defaultVariant is null)
            {
                defaultVariant = descriptor;
            }
        }

        var selected = exactVariant ?? defaultVariant;
        if (selected is null)
        {
            return false;
        }

        var byKey = BuildKeyLookup(entries);
        return TryMaterialize(
            selected,
            byKey,
            new HashSet<string>(StringComparer.Ordinal),
            out theme);
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

    private static bool TryMaterialize(
        SourceGenControlThemeDescriptor descriptor,
        IReadOnlyDictionary<string, SourceGenControlThemeDescriptor> byKey,
        HashSet<string> visitedKeys,
        out ControlTheme? theme)
    {
        theme = null;
        if (descriptor.Factory is null)
        {
            return false;
        }

        var currentTheme = descriptor.Factory();
        if (currentTheme is null)
        {
            return false;
        }

        var descriptorKey = string.IsNullOrWhiteSpace(descriptor.Key)
            ? null
            : descriptor.Key.Trim();
        if (!string.IsNullOrWhiteSpace(descriptorKey) &&
            !visitedKeys.Add(descriptorKey))
        {
            theme = currentTheme;
            return true;
        }

        var basedOnKey = descriptor.BasedOnKey;
        if (!string.IsNullOrWhiteSpace(basedOnKey))
        {
            basedOnKey = basedOnKey.Trim();
            if (byKey.TryGetValue(basedOnKey, out var basedOnDescriptor) &&
                TryMaterialize(basedOnDescriptor, byKey, visitedKeys, out var basedOnTheme))
            {
                currentTheme.BasedOn = basedOnTheme;
            }
        }

        if (!string.IsNullOrWhiteSpace(descriptorKey))
        {
            visitedKeys.Remove(descriptorKey);
        }

        theme = currentTheme;
        return true;
    }

    private static Dictionary<string, SourceGenControlThemeDescriptor> BuildKeyLookup(
        IReadOnlyList<SourceGenControlThemeDescriptor> entries)
    {
        var byKey = new Dictionary<string, SourceGenControlThemeDescriptor>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var key = entries[i].Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            byKey[key.Trim()] = entries[i];
        }

        return byKey;
    }

    private static string? TryExtractBasedOnKey(string? basedOnExpression)
    {
        return StaticResourceReferenceParser.TryExtractResourceKey(
            basedOnExpression,
            out var resourceKey)
            ? resourceKey
            : null;
    }

    private static string? NormalizeThemeVariant(string? themeVariant)
    {
        if (string.IsNullOrWhiteSpace(themeVariant))
        {
            return null;
        }

        var trimmed = themeVariant.Trim();
        if (trimmed.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static bool MatchesTargetType(string? targetTypeName, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(targetTypeName))
        {
            return false;
        }

        var normalizedTypeName = NormalizeTypeName(targetTypeName);
        if (normalizedTypeName.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedTypeName, targetType.Name, StringComparison.Ordinal) ||
            string.Equals(normalizedTypeName, targetType.FullName, StringComparison.Ordinal) ||
            string.Equals(normalizedTypeName, targetType.AssemblyQualifiedName, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeTypeName(string typeName)
    {
        var trimmed = typeName.Trim();
        const string globalPrefix = "global::";
        return trimmed.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? trimmed.Substring(globalPrefix.Length)
            : trimmed;
    }

}
