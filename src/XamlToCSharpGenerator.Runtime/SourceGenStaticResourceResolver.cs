using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenStaticResourceResolver
{
    public static object? Resolve(
        object? anchor,
        object key,
        string currentUri,
        IReadOnlyList<object>? parentStack = null)
    {
        var themeVariant = ResolveThemeVariant(anchor);
        if (themeVariant is null &&
            TryResolveThemeVariantFromParentStack(parentStack, out var parentStackThemeVariant))
        {
            themeVariant = parentStackThemeVariant;
        }

        if (TryResolveFromParentStack(parentStack, key, themeVariant, out var resolvedFromStack))
        {
            return resolvedFromStack;
        }

        if (TryResolveFromContainer(anchor, key, themeVariant, out var resolved))
        {
            return resolved;
        }

        if (anchor is ILogical logical &&
            TryResolveFromLogicalAncestors(logical, key, themeVariant, out resolved))
        {
            return resolved;
        }

        if (TryResolveFromIncludeGraph(key, currentUri, themeVariant, out resolved))
        {
            return resolved;
        }

        if (Application.Current is { } app &&
            app.TryGetResource(key, themeVariant, out var appValue))
        {
            return appValue;
        }

        throw new KeyNotFoundException("Static resource not found: " + key);
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
        out object? value)
    {
        if (string.IsNullOrWhiteSpace(currentUri))
        {
            value = null;
            return false;
        }

        var includes = XamlIncludeGraphRegistry.GetTransitive(currentUri, "MergedDictionaries");
        for (var index = includes.Count - 1; index >= 0; index--)
        {
            var include = includes[index];
            if (XamlSourceGenRegistry.TryCreate(null, include.IncludedUri, out var includedRoot) &&
                TryResolveFromContainer(includedRoot, key, themeVariant, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryResolveFromContainer(
        object? container,
        object key,
        ThemeVariant? themeVariant,
        out object? value)
    {
        if (container is Avalonia.Controls.IResourceNode resourceNode &&
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
