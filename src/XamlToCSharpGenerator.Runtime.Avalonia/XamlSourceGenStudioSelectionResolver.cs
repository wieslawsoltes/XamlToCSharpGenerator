using System;
using System.Collections.Generic;
using global::Avalonia.Controls;

namespace XamlToCSharpGenerator.Runtime;

internal static class XamlSourceGenStudioSelectionResolver
{
    public static bool TryResolveSourceElementForControl(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        string? preferredBuildUri,
        out string? resolvedBuildUri,
        out string? resolvedElementId)
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
            resolvedBuildUri = null;
            resolvedElementId = null;
            return false;
        }

        return XamlSourceGenHotDesignTool.TryResolveElementForLiveSelection(
            controlNames,
            controlTypeNames,
            preferredBuildUri,
            allowAmbiguousTypeFallback: false,
            out resolvedBuildUri,
            out resolvedElementId);
    }

    public static bool TryFindBestMatchingElementNode(
        IReadOnlyCollection<SourceGenHotDesignElementNode> roots,
        IReadOnlyList<string> controlNames,
        IReadOnlyList<string> controlTypeNames,
        out SourceGenHotDesignElementNode? matched)
    {
        var flattened = Flatten(roots);
        return TryFindBestMatchingElementNode(flattened, controlNames, controlTypeNames, out matched);
    }

    public static bool TryFindBestMatchingElementNode(
        IReadOnlyList<SourceGenHotDesignElementNode> flattenedNodes,
        IReadOnlyList<string> controlNames,
        IReadOnlyList<string> controlTypeNames,
        out SourceGenHotDesignElementNode? matched)
    {
        for (var index = 0; index < controlNames.Count; index++)
        {
            var byName = FindByName(flattenedNodes, controlNames[index]);
            if (byName is null)
            {
                continue;
            }

            matched = byName;
            return true;
        }

        for (var index = 0; index < controlTypeNames.Count; index++)
        {
            var byType = FindByTypeName(flattenedNodes, controlTypeNames[index]);
            if (byType is null)
            {
                continue;
            }

            matched = byType;
            return true;
        }

        matched = null;
        return false;
    }

    private static SourceGenHotDesignElementNode? FindByName(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        string controlName)
    {
        SourceGenHotDesignElementNode? best = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (!string.Equals(node.XamlName, controlName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best is null || node.Depth > best.Depth)
            {
                best = node;
            }
        }

        return best;
    }

    private static SourceGenHotDesignElementNode? FindByTypeName(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        string typeName)
    {
        SourceGenHotDesignElementNode? best = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (!string.Equals(node.TypeName, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (best is null || node.Depth > best.Depth)
            {
                best = node;
            }
        }

        return best;
    }

    private static List<SourceGenHotDesignElementNode> Flatten(IReadOnlyCollection<SourceGenHotDesignElementNode> roots)
    {
        var output = new List<SourceGenHotDesignElementNode>(roots.Count * 4);
        foreach (var root in roots)
        {
            FlattenNode(root, output);
        }

        return output;
    }

    private static void FlattenNode(SourceGenHotDesignElementNode node, List<SourceGenHotDesignElementNode> output)
    {
        output.Add(node);
        for (var index = 0; index < node.Children.Count; index++)
        {
            FlattenNode(node.Children[index], output);
        }
    }
}
