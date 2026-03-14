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

        var sourceLookupContext = CreateSourceLookupContext(preferredBuildUri);
        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var root = BuildLiveNode(
            rootControl,
            "live:0",
            0,
            mode,
            sourceLookupContext,
            selectedSourceElementId,
            query,
            sourceLookupContext.LookupCache);

        if (root is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        return [root];
    }

    public static Control? ResolveLiveControlForElement(
        Control? rootControl,
        SourceGenHotDesignHitTestMode mode,
        SourceGenHotDesignElementNode element,
        string? preferredBuildUri)
    {
        if (rootControl is null)
        {
            return null;
        }

        if (element.IsLive &&
            TryResolveControlByLiveNodeId(rootControl, mode, element.Id, out var resolvedByLiveId))
        {
            return resolvedByLiveId;
        }

        if (!string.IsNullOrWhiteSpace(element.SourceElementId))
        {
            var resolvedBySourceElement = FindControlBySourceElement(
                rootControl,
                mode,
                preferredBuildUri,
                element.SourceBuildUri,
                element.SourceElementId);
            if (resolvedBySourceElement is not null)
            {
                return resolvedBySourceElement;
            }
        }

        if (!string.IsNullOrWhiteSpace(element.XamlName))
        {
            var resolvedByName = FindFirstControlByName(rootControl, mode, element.XamlName);
            if (resolvedByName is not null)
            {
                return resolvedByName;
            }
        }

        return string.IsNullOrWhiteSpace(element.TypeName)
            ? null
            : FindUniqueControlByTypeName(rootControl, mode, element.TypeName);
    }

    public static bool TryResolveControlByLiveNodeId(
        Control? rootControl,
        SourceGenHotDesignHitTestMode mode,
        string? liveNodeId,
        out Control? resolvedControl)
    {
        resolvedControl = null;
        if (rootControl is null || !TryParseLiveNodeId(liveNodeId, out var childIndexes))
        {
            return false;
        }

        var current = rootControl;
        for (var index = 0; index < childIndexes.Length; index++)
        {
            var children = EnumerateChildren(current, mode).ToArray();
            var childIndex = childIndexes[index];
            if (childIndex < 0 || childIndex >= children.Length)
            {
                return false;
            }

            current = children[childIndex];
        }

        resolvedControl = current;
        return true;
    }

    private static SourceGenHotDesignElementNode? BuildLiveNode(
        Control control,
        string id,
        int depth,
        SourceGenHotDesignHitTestMode mode,
        SourceElementLookupContext sourceLookupContext,
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
                sourceLookupContext,
                selectedSourceElementId,
                search,
                lookupCache);
            if (childNode is not null)
            {
                childNodes.Add(childNode);
            }
        }

        var sourceMatch = ResolveSourceElement(control, mode, sourceLookupContext, lookupCache);
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

    private static Control? FindControlBySourceElement(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        string? preferredBuildUri,
        string? sourceBuildUri,
        string sourceElementId)
    {
        var lookupContext = CreateSourceLookupContext(preferredBuildUri ?? sourceBuildUri);
        var lookupCache = new Dictionary<string, (string? BuildUri, string? ElementId)>(StringComparer.Ordinal);
        return FindControlBySourceElementCore(
            control,
            mode,
            lookupContext,
            sourceBuildUri,
            sourceElementId,
            lookupCache);
    }

    private static Control? FindControlBySourceElementCore(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        SourceElementLookupContext lookupContext,
        string? sourceBuildUri,
        string sourceElementId,
        Dictionary<string, (string? BuildUri, string? ElementId)> lookupCache)
    {
        var match = ResolveSourceElement(control, mode, lookupContext, lookupCache);
        if (!string.IsNullOrWhiteSpace(match.ElementId) &&
            string.Equals(match.ElementId, sourceElementId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(sourceBuildUri) ||
             string.Equals(match.BuildUri, sourceBuildUri, StringComparison.OrdinalIgnoreCase)))
        {
            return control;
        }

        foreach (var child in EnumerateChildren(control, mode))
        {
            var resolved = FindControlBySourceElementCore(
                child,
                mode,
                lookupContext,
                sourceBuildUri,
                sourceElementId,
                lookupCache);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static Control? FindFirstControlByName(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        string name)
    {
        if (string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return control;
        }

        foreach (var child in EnumerateChildren(control, mode))
        {
            var resolved = FindFirstControlByName(child, mode, name);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static Control? FindUniqueControlByTypeName(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        string typeName)
    {
        Control? match = null;
        var ambiguous = false;
        CollectMatchingControlsByTypeName(control, mode, typeName, ref match, ref ambiguous);
        return ambiguous ? null : match;
    }

    private static void CollectMatchingControlsByTypeName(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        string typeName,
        ref Control? match,
        ref bool ambiguous)
    {
        if (ambiguous)
        {
            return;
        }

        if (string.Equals(control.GetType().Name, typeName, StringComparison.Ordinal))
        {
            if (match is not null)
            {
                ambiguous = true;
                return;
            }

            match = control;
        }

        foreach (var child in EnumerateChildren(control, mode))
        {
            CollectMatchingControlsByTypeName(child, mode, typeName, ref match, ref ambiguous);
            if (ambiguous)
            {
                return;
            }
        }
    }

    private static bool TryParseLiveNodeId(string? liveNodeId, out int[] childIndexes)
    {
        childIndexes = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(liveNodeId) ||
            !liveNodeId.StartsWith("live:", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = liveNodeId["live:".Length..].Split('/');
        if (tokens.Length == 0 || !string.Equals(tokens[0], "0", StringComparison.Ordinal))
        {
            return false;
        }

        if (tokens.Length == 1)
        {
            return true;
        }

        var parsedIndexes = new int[tokens.Length - 1];
        for (var index = 1; index < tokens.Length; index++)
        {
            if (!int.TryParse(tokens[index], out parsedIndexes[index - 1]) || parsedIndexes[index - 1] < 0)
            {
                childIndexes = Array.Empty<int>();
                return false;
            }
        }

        childIndexes = parsedIndexes;
        return true;
    }

    private static (string? BuildUri, string? ElementId) ResolveSourceElement(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        SourceElementLookupContext sourceLookupContext,
        IDictionary<string, (string? BuildUri, string? ElementId)> cache)
    {
        var controlNames = new List<string>(4);
        var controlTypeNames = new List<string>(6);
        XamlSourceGenStudioHitTestingService.CollectIdentityCandidates(
            control,
            mode,
            controlNames,
            controlTypeNames);

        if (controlNames.Count == 0 && controlTypeNames.Count == 0)
        {
            return (null, null);
        }

        var key = BuildLookupKey(sourceLookupContext.PreferredBuildUri, controlNames, controlTypeNames);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (sourceLookupContext.FlattenedPreferredElements.Count > 0 &&
            XamlSourceGenStudioSelectionResolver.TryFindBestMatchingElementNode(
                sourceLookupContext.FlattenedPreferredElements,
                controlNames,
                controlTypeNames,
                out var matchedInPreferredDocument))
        {
            var resolved = (sourceLookupContext.PreferredBuildUri, matchedInPreferredDocument!.Id);
            cache[key] = resolved;
            return resolved;
        }

        if (!XamlSourceGenHotDesignTool.TryResolveElementForLiveSelection(
                controlNames,
                controlTypeNames,
                sourceLookupContext.PreferredBuildUri,
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

    private static SourceElementLookupContext CreateSourceLookupContext(string? preferredBuildUri)
    {
        if (!string.IsNullOrWhiteSpace(preferredBuildUri) &&
            XamlSourceGenHotDesignCoreTools.TryBuildElementTreeForDocument(preferredBuildUri, out var preferredElements))
        {
            return new SourceElementLookupContext(
                preferredBuildUri,
                FlattenElementTree(preferredElements));
        }

        return new SourceElementLookupContext(preferredBuildUri, Array.Empty<SourceGenHotDesignElementNode>());
    }

    private static List<SourceGenHotDesignElementNode> FlattenElementTree(
        IReadOnlyList<SourceGenHotDesignElementNode> roots)
    {
        if (roots.Count == 0)
        {
            return [];
        }

        var flattened = new List<SourceGenHotDesignElementNode>(64);
        for (var index = 0; index < roots.Count; index++)
        {
            FlattenElementNode(roots[index], flattened);
        }

        return flattened;
    }

    private static void FlattenElementNode(
        SourceGenHotDesignElementNode node,
        List<SourceGenHotDesignElementNode> flattened)
    {
        flattened.Add(node);
        for (var index = 0; index < node.Children.Count; index++)
        {
            FlattenElementNode(node.Children[index], flattened);
        }
    }

    private static string BuildLookupKey(
        string? preferredBuildUri,
        IReadOnlyList<string> controlNames,
        IReadOnlyList<string> controlTypeNames)
    {
        var key = preferredBuildUri ?? string.Empty;
        for (var index = 0; index < controlNames.Count; index++)
        {
            key += "|n:" + controlNames[index];
        }

        for (var index = 0; index < controlTypeNames.Count; index++)
        {
            key += "|t:" + controlTypeNames[index];
        }

        return key;
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

    private sealed class SourceElementLookupContext
    {
        public SourceElementLookupContext(
            string? preferredBuildUri,
            IReadOnlyList<SourceGenHotDesignElementNode> flattenedPreferredElements)
        {
            PreferredBuildUri = preferredBuildUri;
            FlattenedPreferredElements = flattenedPreferredElements;
        }

        public string? PreferredBuildUri { get; }

        public IReadOnlyList<SourceGenHotDesignElementNode> FlattenedPreferredElements { get; }

        public Dictionary<string, (string? BuildUri, string? ElementId)> LookupCache { get; } =
            new(StringComparer.Ordinal);
    }
}
