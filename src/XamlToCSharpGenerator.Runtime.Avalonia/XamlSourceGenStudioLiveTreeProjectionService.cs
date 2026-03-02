using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace XamlToCSharpGenerator.Runtime;

internal static class XamlSourceGenStudioLiveTreeProjectionService
{
    public static IReadOnlyList<SourceGenHotDesignElementNode> BuildLiveTree(
        Control? rootControl,
        SourceGenHotDesignHitTestMode mode,
        string? preferredBuildUri,
        string? selectedSourceElementId,
        string? search = null)
    {
        if (rootControl is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        var lookupCache = new Dictionary<string, (string? BuildUri, string? ElementId)>(StringComparer.Ordinal);
        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var root = BuildLiveNode(
            rootControl,
            "live:0",
            0,
            mode,
            preferredBuildUri,
            selectedSourceElementId,
            query,
            lookupCache);

        if (root is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        return [root];
    }

    private static SourceGenHotDesignElementNode? BuildLiveNode(
        Control control,
        string id,
        int depth,
        SourceGenHotDesignHitTestMode mode,
        string? preferredBuildUri,
        string? selectedSourceElementId,
        string? search,
        Dictionary<string, (string? BuildUri, string? ElementId)> lookupCache)
    {
        var children = EnumerateChildren(control, mode).ToArray();
        var childNodes = new List<SourceGenHotDesignElementNode>(children.Length);
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            var childNode = BuildLiveNode(
                child,
                id + "/" + index,
                depth + 1,
                mode,
                preferredBuildUri,
                selectedSourceElementId,
                search,
                lookupCache);
            if (childNode is not null)
            {
                childNodes.Add(childNode);
            }
        }

        var sourceMatch = ResolveSourceElement(control, preferredBuildUri, lookupCache);
        var sourceBuildUri = sourceMatch.BuildUri;
        var sourceElementId = sourceMatch.ElementId;

        var isSelected = !string.IsNullOrWhiteSpace(selectedSourceElementId) &&
                         !string.IsNullOrWhiteSpace(sourceElementId) &&
                         string.Equals(sourceElementId, selectedSourceElementId, StringComparison.Ordinal);

        var classes = control.Classes.Count > 0
            ? string.Join(" ", control.Classes)
            : null;
        var displayName = BuildDisplayName(control);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var matches = displayName.Contains(search!, StringComparison.OrdinalIgnoreCase) ||
                          control.GetType().Name.Contains(search!, StringComparison.OrdinalIgnoreCase) ||
                          (control.Name?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!matches && childNodes.Count == 0)
            {
                return null;
            }
        }

        return new SourceGenHotDesignElementNode(
            Id: id,
            DisplayName: displayName,
            TypeName: control.GetType().Name,
            XamlName: string.IsNullOrWhiteSpace(control.Name) ? null : control.Name,
            Classes: classes,
            Depth: depth,
            IsSelected: isSelected,
            Line: 0,
            Children: childNodes,
            IsExpanded: depth <= 1 || isSelected || !string.IsNullOrWhiteSpace(search),
            DescendantCount: CountDescendants(childNodes),
            SourceBuildUri: sourceBuildUri,
            SourceElementId: sourceElementId,
            IsLive: true);
    }

    private static IEnumerable<Control> EnumerateChildren(Control control, SourceGenHotDesignHitTestMode mode)
    {
        if (mode == SourceGenHotDesignHitTestMode.Logical)
        {
            foreach (var child in ((ILogical)control).GetLogicalChildren())
            {
                if (child is Control childControl)
                {
                    yield return childControl;
                }
            }

            yield break;
        }

        foreach (var visualChild in control.GetVisualChildren())
        {
            if (visualChild is Control childControl)
            {
                yield return childControl;
            }
        }
    }

    private static (string? BuildUri, string? ElementId) ResolveSourceElement(
        Control control,
        string? preferredBuildUri,
        IDictionary<string, (string? BuildUri, string? ElementId)> cache)
    {
        var key = (preferredBuildUri ?? string.Empty) + "|" + (control.Name ?? string.Empty) + "|" + control.GetType().FullName;
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var names = string.IsNullOrWhiteSpace(control.Name)
            ? Array.Empty<string>()
            : new[] { control.Name };
        var types = new[] { control.GetType().Name };
        if (!XamlSourceGenHotDesignTool.TryResolveElementForLiveSelection(
                names,
                types,
                preferredBuildUri,
                allowAmbiguousTypeFallback: true,
                out var resolvedBuildUri,
                out var resolvedElementId) ||
            string.IsNullOrWhiteSpace(resolvedElementId))
        {
            cache[key] = (null, null);
            return (null, null);
        }

        cache[key] = (resolvedBuildUri, resolvedElementId);
        return (resolvedBuildUri, resolvedElementId);
    }

    private static string BuildDisplayName(Control control)
    {
        var typeName = control.GetType().Name;
        if (string.IsNullOrWhiteSpace(control.Name))
        {
            return "[" + typeName + "]";
        }

        return "[" + typeName + "] " + control.Name;
    }

    private static int CountDescendants(IReadOnlyList<SourceGenHotDesignElementNode> children)
    {
        if (children.Count == 0)
        {
            return 0;
        }

        var count = 0;
        for (var index = 0; index < children.Count; index++)
        {
            count += 1 + children[index].DescendantCount;
        }

        return count;
    }
}
