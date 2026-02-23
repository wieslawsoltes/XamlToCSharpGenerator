using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenStaticResourceResolver
{
    private static readonly AsyncLocal<HashSet<string>?> ActiveResolutionFrames = new();
    private static readonly AsyncLocal<HashSet<string>?> ActiveResolutionUris = new();
    private static readonly AsyncLocal<HashSet<string>?> ActiveUriMaterializations = new();

    public static object? Resolve(
        object? anchor,
        object key,
        string currentUri,
        IServiceProvider? serviceProvider = null,
        IReadOnlyList<object>? parentStack = null)
    {
        if (TryResolve(anchor, key, currentUri, out var resolvedValue, serviceProvider, parentStack))
        {
            return resolvedValue;
        }

        throw new KeyNotFoundException("Static resource not found: " + key);
    }

    public static bool TryResolve(
        object? anchor,
        object key,
        string? currentUri,
        out object? value,
        IServiceProvider? serviceProvider = null,
        IReadOnlyList<object>? parentStack = null)
    {
        var uri = currentUri ?? string.Empty;
        var resolutionFrameKey = BuildResolutionFrameKey(uri, key);
        var enteredResolutionFrame = TryEnterResolutionFrame(resolutionFrameKey);
        var enteredResolutionUri = TryEnterResolutionUri(uri);
        try
        {
            var themeVariant = ResolveThemeVariant(anchor);
            if (themeVariant is null &&
                TryResolveThemeVariantFromParentStack(parentStack, out var parentStackThemeVariant))
            {
                themeVariant = parentStackThemeVariant;
            }

            if (TryResolveFromParentStack(parentStack, key, themeVariant, out var resolvedFromStack))
            {
                value = resolvedFromStack;
                return true;
            }

            if (TryResolveFromContainer(anchor, key, themeVariant, out var resolved))
            {
                value = resolved;
                return true;
            }

            if (anchor is ILogical logical &&
                TryResolveFromLogicalAncestors(logical, key, themeVariant, out resolved))
            {
                value = resolved;
                return true;
            }

            if (enteredResolutionFrame && enteredResolutionUri)
            {
                if (TryResolveFromIncludeGraph(key, uri, themeVariant, serviceProvider, out resolved))
                {
                    value = resolved;
                    return true;
                }

                if (TryResolveFromOwningIncludeGraphs(key, uri, themeVariant, serviceProvider, out resolved))
                {
                    value = resolved;
                    return true;
                }
            }

            if (Application.Current is { } app &&
                app.TryGetResource(key, themeVariant, out var appValue))
            {
                value = appValue;
                return true;
            }

            value = null;
            return false;
        }
        finally
        {
            if (enteredResolutionFrame)
            {
                ExitResolutionFrame(resolutionFrameKey);
            }

            if (enteredResolutionUri)
            {
                ExitResolutionUri(uri);
            }
        }
    }

    private static string BuildResolutionFrameKey(string uri, object resourceKey) =>
        string.Concat(uri ?? string.Empty, "|", resourceKey?.ToString() ?? string.Empty);

    private static bool TryEnterResolutionFrame(string frameKey)
    {
        if (string.IsNullOrWhiteSpace(frameKey))
        {
            return false;
        }

        var frames = ActiveResolutionFrames.Value ??= new HashSet<string>(StringComparer.Ordinal);
        if (!frames.Add(frameKey))
        {
            return false;
        }

        return true;
    }

    private static void ExitResolutionFrame(string frameKey)
    {
        var frames = ActiveResolutionFrames.Value;
        if (frames is null)
        {
            return;
        }

        frames.Remove(frameKey);
        if (frames.Count == 0)
        {
            ActiveResolutionFrames.Value = null;
        }
    }

    private static bool TryEnterResolutionUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        var uris = ActiveResolutionUris.Value ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!uris.Add(uri))
        {
            return false;
        }

        return true;
    }

    private static void ExitResolutionUri(string uri)
    {
        var uris = ActiveResolutionUris.Value;
        if (uris is null)
        {
            return;
        }

        uris.Remove(uri);
        if (uris.Count == 0)
        {
            ActiveResolutionUris.Value = null;
        }
    }

    private static bool TryResolveFromParentStack(
        IReadOnlyList<object>? parentStack,
        object key,
        ThemeVariant? themeVariant,
        out object? value)
    {
        if (parentStack is null || parentStack.Count == 0)
        {
            value = null;
            return false;
        }

        for (var index = 0; index < parentStack.Count; index++)
        {
            if (TryResolveFromContainer(parentStack[index], key, themeVariant, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryResolveFromLogicalAncestors(
        ILogical logical,
        object key,
        ThemeVariant? themeVariant,
        out object? value)
    {
        var current = logical.LogicalParent;
        while (current is not null)
        {
            if (TryResolveFromContainer(current, key, themeVariant, out value))
            {
                return true;
            }

            current = current.LogicalParent;
        }

        value = null;
        return false;
    }

    private static bool TryResolveFromIncludeGraph(
        object key,
        string currentUri,
        ThemeVariant? themeVariant,
        IServiceProvider? serviceProvider,
        out object? value)
    {
        if (string.IsNullOrWhiteSpace(currentUri))
        {
            value = null;
            return false;
        }

        var probedUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includes = XamlIncludeGraphRegistry.GetTransitive(currentUri, "MergedDictionaries");
        for (var index = includes.Count - 1; index >= 0; index--)
        {
            var include = includes[index];
            if (!probedUris.Add(include.IncludedUri) ||
                !ShouldProbeUriForResourceKey(include.IncludedUri, key))
            {
                continue;
            }

            if (TryMaterializeInclude(include.IncludedUri, serviceProvider, out var includedRoot) &&
                TryResolveFromContainer(includedRoot, key, themeVariant, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryResolveFromOwningIncludeGraphs(
        object key,
        string currentUri,
        ThemeVariant? themeVariant,
        IServiceProvider? serviceProvider,
        out object? value)
    {
        if (string.IsNullOrWhiteSpace(currentUri))
        {
            value = null;
            return false;
        }

        var ownerQueue = new Queue<string>();
        var visitedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var probedUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in XamlIncludeGraphRegistry.GetIncoming(currentUri))
        {
            ownerQueue.Enqueue(incoming.SourceUri);
        }

        while (ownerQueue.Count > 0)
        {
            var ownerUri = ownerQueue.Dequeue();
            if (!visitedOwners.Add(ownerUri))
            {
                continue;
            }

            if (probedUris.Add(ownerUri) &&
                ShouldProbeUriForResourceKey(ownerUri, key) &&
                TryMaterializeInclude(ownerUri, serviceProvider, out var ownerRoot) &&
                TryResolveFromContainer(ownerRoot, key, themeVariant, out value))
            {
                return true;
            }

            var mergedIncludes = XamlIncludeGraphRegistry.GetTransitive(ownerUri, "MergedDictionaries");
            for (var index = mergedIncludes.Count - 1; index >= 0; index--)
            {
                var include = mergedIncludes[index];
                if (!probedUris.Add(include.IncludedUri) ||
                    !ShouldProbeUriForResourceKey(include.IncludedUri, key))
                {
                    continue;
                }

                if (TryMaterializeInclude(include.IncludedUri, serviceProvider, out var includedRoot) &&
                    TryResolveFromContainer(includedRoot, key, themeVariant, out value))
                {
                    return true;
                }
            }

            foreach (var ownerIncoming in XamlIncludeGraphRegistry.GetIncoming(ownerUri))
            {
                if (!visitedOwners.Contains(ownerIncoming.SourceUri))
                {
                    ownerQueue.Enqueue(ownerIncoming.SourceUri);
                }
            }
        }

        value = null;
        return false;
    }

    private static bool ShouldProbeUriForResourceKey(string uri, object key)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!TryGetResourceKeyCandidates(key, out var candidates))
        {
            return true;
        }

        if (!XamlResourceRegistry.HasEntries(uri))
        {
            // If the URI is include-only metadata (common for theme aggregation files),
            // probing the URI itself causes recursive re-materialization with no chance
            // of producing a direct resource hit.
            return XamlIncludeGraphRegistry.GetDirect(uri).Count == 0;
        }

        return candidates.Any(candidate => XamlResourceRegistry.ContainsKey(uri, candidate));
    }

    private static bool TryGetResourceKeyCandidates(object key, out IReadOnlyList<string> candidates)
    {
        if (key is string stringKey)
        {
            candidates = new[] { stringKey };
            return true;
        }

        if (key is Type typeKey)
        {
            var values = new HashSet<string>(StringComparer.Ordinal)
            {
                "{x:Type " + typeKey.Name + "}",
                typeKey.Name
            };

            if (!string.IsNullOrWhiteSpace(typeKey.FullName))
            {
                values.Add("{x:Type " + typeKey.FullName + "}");
                values.Add(typeKey.FullName);
            }

            candidates = values.ToArray();
            return true;
        }

        candidates = Array.Empty<string>();
        return false;
    }

    private static bool TryMaterializeInclude(string uri, IServiceProvider? serviceProvider, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        var activeMaterializations = ActiveUriMaterializations.Value ??=
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!activeMaterializations.Add(uri))
        {
            return false;
        }

        try
        {
            return XamlSourceGenRegistry.TryCreate(serviceProvider, uri, out value);
        }
        finally
        {
            activeMaterializations.Remove(uri);
            if (activeMaterializations.Count == 0)
            {
                ActiveUriMaterializations.Value = null;
            }
        }
    }

    private static bool TryResolveFromContainer(
        object? container,
        object key,
        ThemeVariant? themeVariant,
        out object? value)
    {
        if (container is global::Avalonia.Controls.IResourceNode resourceNode &&
            resourceNode.TryGetResource(key, themeVariant, out value))
        {
            return true;
        }

        if (container is IDictionary dictionary &&
            dictionary.Contains(key))
        {
            value = dictionary[key];
            return true;
        }

        value = null;
        return false;
    }

    private static ThemeVariant? ResolveThemeVariant(object? anchor)
    {
        if (anchor is IThemeVariantHost themeVariantHost)
        {
            return themeVariantHost.ActualThemeVariant;
        }

        if (anchor is ILogical logical)
        {
            var current = logical.LogicalParent;
            while (current is not null)
            {
                if (current is IThemeVariantHost ancestorThemeVariantHost)
                {
                    return ancestorThemeVariantHost.ActualThemeVariant;
                }

                current = current.LogicalParent;
            }
        }

        return null;
    }

    private static bool TryResolveThemeVariantFromParentStack(
        IReadOnlyList<object>? parentStack,
        out ThemeVariant? themeVariant)
    {
        if (parentStack is not null)
        {
            for (var index = 0; index < parentStack.Count; index++)
            {
                if (parentStack[index] is IThemeVariantHost provider)
                {
                    themeVariant = provider.ActualThemeVariant;
                    return true;
                }
            }
        }

        themeVariant = null;
        return false;
    }
}
