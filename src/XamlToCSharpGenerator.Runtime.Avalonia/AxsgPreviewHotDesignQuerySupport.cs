using System;
using System.Collections.Generic;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Threading;

namespace XamlToCSharpGenerator.Runtime;

internal static class AxsgPreviewHotDesignQuerySupport
{
    internal static bool TryGetWorkspaceSnapshot(string? buildUri, string? search, out SourceGenHotDesignWorkspaceSnapshot workspace)
    {
        workspace = default!;

        if (!AxsgPreviewHotDesignSessionBridge.TryGetCurrentDocument(
                out var rootControl,
                out var currentBuildUri,
                out var sourcePath,
                out var xamlText) ||
            rootControl is null ||
            string.IsNullOrWhiteSpace(currentBuildUri))
        {
            return false;
        }

        string resolvedBuildUri = string.IsNullOrWhiteSpace(buildUri) ? currentBuildUri! : buildUri.Trim();
        if (!string.Equals(resolvedBuildUri, currentBuildUri, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        workspace = XamlSourceGenHotDesignCoreTools.BuildPreviewWorkspaceSnapshot(
            rootControl.GetType(),
            resolvedBuildUri,
            sourcePath,
            xamlText,
            search);
        return true;
    }

    internal static SourceGenHotDesignLiveTreeSnapshot GetLiveTree(
        SourceGenHotDesignHitTestMode mode,
        string? buildUri,
        string? selectedElementId,
        string? search)
    {
        return InvokeOnUiThread(() =>
        {
            if (!AxsgPreviewHotDesignSessionBridge.TryGetCurrentDocument(out var rootControl, out var currentBuildUri, out _, out _)
                || rootControl is null)
            {
                return new SourceGenHotDesignLiveTreeSnapshot(
                    Mode: mode,
                    ActiveBuildUri: buildUri,
                    SelectedElementId: selectedElementId,
                    Elements: Array.Empty<SourceGenHotDesignElementNode>());
            }

            string? activeBuildUri = string.IsNullOrWhiteSpace(buildUri) ? currentBuildUri : buildUri;
            return new SourceGenHotDesignLiveTreeSnapshot(
                Mode: mode,
                ActiveBuildUri: activeBuildUri,
                SelectedElementId: selectedElementId,
                Elements: XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                    rootControl,
                    mode,
                    activeBuildUri,
                    selectedElementId,
                    search));
        });
    }

    internal static SourceGenHotDesignOverlaySnapshot GetOverlaySnapshot(
        SourceGenHotDesignHitTestMode mode,
        string? activeBuildUri,
        string? selectedElementId,
        SourceGenHotDesignElementNode? selectedElement)
    {
        return InvokeOnUiThread(() =>
        {
            AxsgPreviewHotDesignSessionBridge.TryGetCurrentDocument(out var rootControl, out var currentBuildUri, out _, out _);
            string? resolvedBuildUri = string.IsNullOrWhiteSpace(activeBuildUri) ? currentBuildUri : activeBuildUri;

            SourceGenHotDesignElementNode? hoverElement = null;
            if (AxsgPreviewHotDesignSessionBridge.TryGetHoverElement(out var hoverBuildUri, out var hoverElementId))
            {
                SourceGenHotDesignWorkspaceSnapshot hoverWorkspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(hoverBuildUri, search: null);
                hoverElement = FindElement(hoverWorkspace.Elements, hoverElementId);
            }

            return new SourceGenHotDesignOverlaySnapshot(
                Mode: mode,
                ActiveBuildUri: resolvedBuildUri,
                SelectedElementId: selectedElementId,
                RootWidth: rootControl?.Bounds.Width ?? 0,
                RootHeight: rootControl?.Bounds.Height ?? 0,
                Selected: BuildOverlayItem(rootControl, mode, resolvedBuildUri, selectedElement),
                Hover: BuildOverlayItem(rootControl, mode, hoverElement?.SourceBuildUri ?? resolvedBuildUri, hoverElement));
        });
    }

    internal static SourceGenHotDesignHitTestResult HitTestAtPoint(
        double x,
        double y,
        string? buildUri,
        SourceGenHotDesignHitTestMode mode,
        bool updateSelection)
    {
        return InvokeOnUiThread(() =>
        {
            SourceGenHotDesignWorkspaceSnapshot activeWorkspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search: null);
            if (!AxsgPreviewHotDesignSessionBridge.TryGetCurrentDocument(out var rootControl, out var currentBuildUri, out _, out _)
                || rootControl is null)
            {
                return new SourceGenHotDesignHitTestResult(
                    Succeeded: false,
                    SelectionChanged: false,
                    ActiveBuildUri: activeWorkspace.ActiveBuildUri ?? buildUri ?? currentBuildUri,
                    ElementId: null,
                    Element: null,
                    Overlay: GetOverlaySnapshot(
                        mode,
                        activeWorkspace.ActiveBuildUri ?? buildUri ?? currentBuildUri,
                        activeWorkspace.SelectedElementId,
                        FindElement(activeWorkspace.Elements, activeWorkspace.SelectedElementId)),
                    Message: "No active preview root is available.");
            }

            Control? control = XamlSourceGenStudioHitTestingService.ResolveControlAtPoint(rootControl, new Point(x, y), mode);
            if (control is null ||
                !XamlSourceGenStudioSelectionResolver.TryResolveSourceElementForControl(
                    control,
                    mode,
                    buildUri ?? currentBuildUri,
                    out var resolvedBuildUri,
                    out var resolvedElementId) ||
                string.IsNullOrWhiteSpace(resolvedBuildUri) ||
                string.IsNullOrWhiteSpace(resolvedElementId))
            {
                AxsgPreviewHotDesignSessionBridge.ClearHoverElement();

                SourceGenHotDesignWorkspaceSnapshot workspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri ?? currentBuildUri, search: null);
                return new SourceGenHotDesignHitTestResult(
                    Succeeded: false,
                    SelectionChanged: false,
                    ActiveBuildUri: workspace.ActiveBuildUri ?? buildUri ?? currentBuildUri,
                    ElementId: null,
                    Element: null,
                    Overlay: GetOverlaySnapshot(
                        mode,
                        workspace.ActiveBuildUri ?? buildUri ?? currentBuildUri,
                        workspace.SelectedElementId,
                        FindElement(workspace.Elements, workspace.SelectedElementId)),
                    Message: "No selectable element was found at the requested point.");
            }

            SourceGenHotDesignWorkspaceSnapshot workspaceBefore = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(resolvedBuildUri, search: null);
            bool selectionChanged = !string.Equals(workspaceBefore.SelectedElementId, resolvedElementId, StringComparison.Ordinal);

            if (updateSelection)
            {
                AxsgPreviewHotDesignSessionBridge.ClearHoverElement();
                XamlSourceGenHotDesignTool.SelectElement(resolvedBuildUri, resolvedElementId);
            }
            else
            {
                AxsgPreviewHotDesignSessionBridge.UpdateHoverElement(resolvedBuildUri, resolvedElementId);
            }

            SourceGenHotDesignWorkspaceSnapshot workspaceAfter = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(resolvedBuildUri, search: null);
            SourceGenHotDesignElementNode? selectedElement = FindElement(workspaceAfter.Elements, workspaceAfter.SelectedElementId);
            SourceGenHotDesignElementNode? hitElement = FindElement(workspaceAfter.Elements, resolvedElementId);

            return new SourceGenHotDesignHitTestResult(
                Succeeded: true,
                SelectionChanged: updateSelection && selectionChanged,
                ActiveBuildUri: workspaceAfter.ActiveBuildUri ?? resolvedBuildUri,
                ElementId: resolvedElementId,
                Element: hitElement,
                Overlay: GetOverlaySnapshot(
                    mode,
                    workspaceAfter.ActiveBuildUri ?? resolvedBuildUri,
                    workspaceAfter.SelectedElementId,
                    selectedElement));
        });
    }

    private static T InvokeOnUiThread<T>(Func<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        T? result = default;

        void Execute()
        {
            result = callback();
        }

        if (SourceGenDispatcherRuntime.TryInvoke(Execute, DispatcherPriority.Background))
        {
            return result!;
        }

        Dispatcher dispatcher = Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
        {
            return callback();
        }

        return dispatcher.InvokeAsync(callback, DispatcherPriority.Background).GetAwaiter().GetResult();
    }

    internal static SourceGenHotDesignElementNode? FindElement(
        IReadOnlyList<SourceGenHotDesignElementNode> elements,
        string? elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        for (int index = 0; index < elements.Count; index++)
        {
            SourceGenHotDesignElementNode element = elements[index];
            if (string.Equals(element.Id, elementId, StringComparison.Ordinal))
            {
                return element;
            }

            SourceGenHotDesignElementNode? child = FindElement(element.Children, elementId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static SourceGenHotDesignOverlayItem? BuildOverlayItem(
        Control? rootControl,
        SourceGenHotDesignHitTestMode mode,
        string? preferredBuildUri,
        SourceGenHotDesignElementNode? element)
    {
        if (rootControl is null || element is null)
        {
            return null;
        }

        Control? liveControl = XamlSourceGenStudioLiveTreeProjectionService.ResolveLiveControlForElement(
            rootControl,
            mode,
            element,
            preferredBuildUri);
        if (liveControl is null ||
            !XamlSourceGenStudioHitTestingService.TryGetControlBoundsInLayer(liveControl, rootControl, out Rect bounds))
        {
            return new SourceGenHotDesignOverlayItem(
                ActiveBuildUri: element.SourceBuildUri ?? preferredBuildUri,
                ElementId: element.SourceElementId ?? element.Id,
                Element: element,
                Bounds: null,
                DisplayLabel: element.DisplayName);
        }

        return new SourceGenHotDesignOverlayItem(
            ActiveBuildUri: element.SourceBuildUri ?? preferredBuildUri,
            ElementId: element.SourceElementId ?? element.Id,
            Element: element,
            Bounds: new SourceGenHotDesignOverlayBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            DisplayLabel: element.DisplayName);
    }
}
