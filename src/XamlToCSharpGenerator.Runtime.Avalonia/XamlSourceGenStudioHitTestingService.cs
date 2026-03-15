using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace XamlToCSharpGenerator.Runtime;

internal static class XamlSourceGenStudioHitTestingService
{
    public static Control? ResolveControlAtPoint(
        Grid liveLayer,
        ContentControl livePresenter,
        Point point,
        SourceGenHotDesignHitTestMode mode)
    {
        var logicalRoot = livePresenter.Content as StyledElement;
        foreach (var visual in liveLayer.GetVisualsAt(point))
        {
            if (visual is not Control control || !IsHitCandidate(control, livePresenter))
            {
                continue;
            }

            if (mode == SourceGenHotDesignHitTestMode.Visual || logicalRoot is null)
            {
                return control;
            }

            var logicalSelection = ResolveLogicalSelectionControl(control, logicalRoot);
            if (logicalSelection is not null)
            {
                return logicalSelection;
            }
        }

        return null;
    }

    public static void CollectIdentityCandidates(
        Control control,
        SourceGenHotDesignHitTestMode mode,
        ICollection<string> controlNames,
        ICollection<string> controlTypeNames)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        if (mode == SourceGenHotDesignHitTestMode.Visual)
        {
            AddIdentityFromVisualChain(control, seenNames, seenTypes, controlNames, controlTypeNames);
            return;
        }

        AddIdentityFromLogicalChain(control, seenNames, seenTypes, controlNames, controlTypeNames);
    }

    public static bool TryGetControlBoundsInLayer(Control control, Visual layer, out Rect bounds)
    {
        bounds = default;

        if (!control.IsEffectivelyVisible ||
            !control.IsHitTestVisible ||
            control.Bounds.Width <= 0 ||
            control.Bounds.Height <= 0)
        {
            return false;
        }

        var matrix = control.TransformToVisual(layer);
        if (!matrix.HasValue)
        {
            return false;
        }

        var transformedBounds = new Rect(control.Bounds.Size).TransformToAABB(matrix.Value);
        if (transformedBounds.Width <= 0 || transformedBounds.Height <= 0)
        {
            return false;
        }

        var clipped = transformedBounds;
        var current = control as Visual;
        while (current is not null && !ReferenceEquals(current, layer))
        {
            var parent = current.GetVisualParent();
            if (parent is null)
            {
                break;
            }

            if (parent.ClipToBounds)
            {
                var parentMatrix = parent.TransformToVisual(layer);
                if (!parentMatrix.HasValue)
                {
                    return false;
                }

                var parentBounds = new Rect(parent.Bounds.Size).TransformToAABB(parentMatrix.Value);
                clipped = clipped.Intersect(parentBounds);
                if (clipped.Width <= 0 || clipped.Height <= 0)
                {
                    return false;
                }
            }

            current = parent;
        }

        var layerBounds = layer.Bounds;
        clipped = clipped.Intersect(layerBounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return false;
        }

        bounds = AlignToPixelBounds(clipped);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static bool IsDescendantOf(Visual candidate, Visual ancestor)
    {
        var current = candidate;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static bool IsHitCandidate(Visual visual, ContentControl livePresenter)
    {
        return visual is Control control &&
               control.IsVisible &&
               control.IsEffectivelyVisible &&
               control.IsHitTestVisible &&
               IsDescendantOf(control, livePresenter);
    }

    internal static Control? ResolveLogicalSelectionControl(Control control, StyledElement logicalRoot)
    {
        Control? unnamedFallback = null;
        StyledElement? current = control;
        while (current is not null)
        {
            if (current is Control currentControl && IsLogicalDescendant(currentControl, logicalRoot))
            {
                if (!string.IsNullOrWhiteSpace(currentControl.Name))
                {
                    return currentControl;
                }

                unnamedFallback ??= currentControl;
            }

            current = current.Parent as StyledElement;
        }

        var visual = control as Visual;
        while (visual is not null)
        {
            if (visual is Control visualControl && IsLogicalDescendant(visualControl, logicalRoot))
            {
                if (!string.IsNullOrWhiteSpace(visualControl.Name))
                {
                    return visualControl;
                }

                unnamedFallback ??= visualControl;
            }

            visual = visual.GetVisualParent();
        }

        return unnamedFallback;
    }

    private static bool IsLogicalDescendant(Control control, StyledElement logicalRoot)
    {
        StyledElement? current = control;
        while (current is not null)
        {
            if (ReferenceEquals(current, logicalRoot))
            {
                return true;
            }

            current = current.Parent as StyledElement;
        }

        return false;
    }

    private static void AddIdentityFromVisualChain(
        Control control,
        ISet<string> seenNames,
        ISet<string> seenTypes,
        ICollection<string> controlNames,
        ICollection<string> controlTypeNames)
    {
        if (!string.IsNullOrWhiteSpace(control.Name) && seenNames.Add(control.Name))
        {
            controlNames.Add(control.Name);
        }

        var selfTypeName = control.GetType().Name;
        if (!string.IsNullOrWhiteSpace(selfTypeName) && seenTypes.Add(selfTypeName))
        {
            controlTypeNames.Add(selfTypeName);
        }

        foreach (var ancestor in control.GetVisualAncestors())
        {
            if (ancestor is not Control ancestorControl)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ancestorControl.Name) && seenNames.Add(ancestorControl.Name))
            {
                controlNames.Add(ancestorControl.Name);
            }

            var ancestorTypeName = ancestorControl.GetType().Name;
            if (!string.IsNullOrWhiteSpace(ancestorTypeName) && seenTypes.Add(ancestorTypeName))
            {
                controlTypeNames.Add(ancestorTypeName);
            }
        }
    }

    private static void AddIdentityFromLogicalChain(
        Control control,
        ISet<string> seenNames,
        ISet<string> seenTypes,
        ICollection<string> controlNames,
        ICollection<string> controlTypeNames)
    {
        var current = control;
        while (current is not null)
        {
            var controlName = current.Name;
            if (!string.IsNullOrWhiteSpace(controlName) && seenNames.Add(controlName))
            {
                controlNames.Add(controlName);
            }

            var typeName = current.GetType().Name;
            if (!string.IsNullOrWhiteSpace(typeName) && seenTypes.Add(typeName))
            {
                controlTypeNames.Add(typeName);
            }

            current = current.Parent as Control;
        }
    }

    private static Rect AlignToPixelBounds(Rect rect)
    {
        var left = Math.Floor(rect.Left);
        var top = Math.Floor(rect.Top);
        var right = Math.Ceiling(rect.Right);
        var bottom = Math.Ceiling(rect.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
