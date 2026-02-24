using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using System.Xml;
using Avalonia;
using Avalonia.Controls;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotDesignCoreTools
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DocumentHistoryState> Histories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type?> TypeResolutionCache = new(StringComparer.Ordinal);

    private static SourceGenHotDesignWorkspaceMode WorkspaceMode = SourceGenHotDesignWorkspaceMode.Design;
    private static SourceGenHotDesignPropertyFilterMode PropertyFilterMode = SourceGenHotDesignPropertyFilterMode.Smart;
    private static SourceGenHotDesignPanelState PanelState = new();
    private static SourceGenHotDesignCanvasState CanvasState = new();
    private static string? ActiveBuildUri;
    private static string? SelectedElementId;

    public static void ResetWorkspace()
    {
        lock (Sync)
        {
            Histories.Clear();
            TypeResolutionCache.Clear();
            WorkspaceMode = SourceGenHotDesignWorkspaceMode.Design;
            PropertyFilterMode = SourceGenHotDesignPropertyFilterMode.Smart;
            PanelState = new SourceGenHotDesignPanelState();
            CanvasState = new SourceGenHotDesignCanvasState();
            ActiveBuildUri = null;
            SelectedElementId = null;
        }
    }

    public static SourceGenHotDesignWorkspaceSnapshot GetWorkspaceSnapshot(string? buildUri = null, string? search = null)
    {
        var status = XamlSourceGenHotDesignManager.GetStatus();
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();

        string? currentActiveBuildUri;
        string? currentSelectedElementId;
        SourceGenHotDesignWorkspaceMode currentMode;
        SourceGenHotDesignPropertyFilterMode currentPropertyFilterMode;
        SourceGenHotDesignPanelState currentPanelState;
        SourceGenHotDesignCanvasState currentCanvasState;

        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(buildUri))
            {
                ActiveBuildUri = buildUri.Trim();
            }

            if (ActiveBuildUri is null)
            {
                ActiveBuildUri = SelectDefaultDocument(documents)?.BuildUri;
            }

            currentActiveBuildUri = ActiveBuildUri;
            currentSelectedElementId = SelectedElementId;
            currentMode = WorkspaceMode;
            currentPropertyFilterMode = PropertyFilterMode;
            currentPanelState = PanelState.Clone();
            currentCanvasState = CanvasState.Clone();
        }

        var selectedDocument = ResolveDocument(documents, currentActiveBuildUri, null, null);
        var currentText = selectedDocument is null ? null : ReadCurrentXamlText(selectedDocument, status.Options.MaxHistoryEntries);

        var elements = BuildElementTree(currentText, currentSelectedElementId, search, out var selectionExists);
        if (!selectionExists)
        {
            currentSelectedElementId = SelectPreferredElementId(elements);
            lock (Sync)
            {
                SelectedElementId = currentSelectedElementId;
            }
        }

        var properties = BuildPropertyEntries(currentText, currentSelectedElementId, currentPropertyFilterMode);

        bool canUndo;
        bool canRedo;
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(currentActiveBuildUri) &&
                Histories.TryGetValue(currentActiveBuildUri, out var history))
            {
                canUndo = history.UndoStack.Count > 0;
                canRedo = history.RedoStack.Count > 0;
            }
            else
            {
                canUndo = false;
                canRedo = false;
            }
        }

        return new SourceGenHotDesignWorkspaceSnapshot(
            Status: status,
            Mode: currentMode,
            PropertyFilterMode: currentPropertyFilterMode,
            Panels: currentPanelState,
            Canvas: currentCanvasState,
            ActiveBuildUri: currentActiveBuildUri,
            SelectedElementId: currentSelectedElementId,
            CanUndo: canUndo,
            CanRedo: canRedo,
            CurrentXamlText: currentText,
            Documents: documents,
            Elements: elements,
            Properties: properties,
            Toolbox: BuildToolboxCategories(search));
    }

    public static bool TryResolveElementForLiveSelection(
        IReadOnlyList<string>? controlNames,
        IReadOnlyList<string>? controlTypeNames,
        out string? buildUri,
        out string? elementId)
    {
        buildUri = null;
        elementId = null;

        if ((controlNames is null || controlNames.Count == 0) &&
            (controlTypeNames is null || controlTypeNames.Count == 0))
        {
            return false;
        }

        var resolvedControlNames = controlNames ?? Array.Empty<string>();
        var resolvedControlTypeNames = controlTypeNames ?? Array.Empty<string>();

        var status = XamlSourceGenHotDesignManager.GetStatus();
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        if (documents.Count == 0)
        {
            return false;
        }

        foreach (var document in documents.OrderBy(GetDefaultDocumentScore))
        {
            var text = ReadCurrentXamlText(document, status.Options.MaxHistoryEntries);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var elements = BuildElementTree(text, selectedElementId: null, search: null, out _);
            if (elements.Count == 0)
            {
                continue;
            }

            var flattened = FlattenElementTree(elements);

            if (TryFindMatchingElementByName(flattened, resolvedControlNames, out var matchedByName))
            {
                buildUri = document.BuildUri;
                elementId = matchedByName!.Id;
                return true;
            }

            if (TryFindMatchingElementByType(flattened, resolvedControlTypeNames, out var matchedByType))
            {
                buildUri = document.BuildUri;
                elementId = matchedByType!.Id;
                return true;
            }
        }

        return false;
    }

    public static void SetWorkspaceMode(SourceGenHotDesignWorkspaceMode mode)
    {
        lock (Sync)
        {
            WorkspaceMode = mode;
        }
    }

    public static void SetPropertyFilterMode(SourceGenHotDesignPropertyFilterMode mode)
    {
        lock (Sync)
        {
            PropertyFilterMode = mode;
        }
    }

    public static bool TogglePanel(SourceGenHotDesignPanelKind panel)
    {
        lock (Sync)
        {
            return PanelState.Toggle(panel);
        }
    }

    public static void SetPanelVisibility(SourceGenHotDesignPanelKind panel, bool visible)
    {
        lock (Sync)
        {
            PanelState.SetVisible(panel, visible);
        }
    }

    public static void SetCanvasZoom(double zoom)
    {
        lock (Sync)
        {
            CanvasState.SetZoom(zoom);
        }
    }

    public static void SetCanvasFormFactor(string formFactor, double? width = null, double? height = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formFactor);

        lock (Sync)
        {
            CanvasState.FormFactor = formFactor.Trim();
            if (width.HasValue && width.Value > 0)
            {
                CanvasState.Width = width.Value;
            }

            if (height.HasValue && height.Value > 0)
            {
                CanvasState.Height = height.Value;
            }
        }
    }

    public static void SetCanvasTheme(bool darkTheme)
    {
        lock (Sync)
        {
            CanvasState.DarkTheme = darkTheme;
        }
    }

    public static void SelectDocument(string? buildUri)
    {
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            return;
        }

        lock (Sync)
        {
            ActiveBuildUri = buildUri.Trim();
            SelectedElementId = null;
        }
    }

    public static void SelectElement(string? buildUri, string? elementId)
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(buildUri))
            {
                ActiveBuildUri = buildUri.Trim();
            }

            SelectedElementId = string.IsNullOrWhiteSpace(elementId) ? null : elementId.Trim();
        }
    }

    public static ValueTask<SourceGenHotDesignApplyResult> ApplyDocumentTextAsync(
        string buildUri,
        string xamlText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildUri);
        ArgumentNullException.ThrowIfNull(xamlText);

        var request = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = buildUri,
            XamlText = xamlText
        };

        return ApplyUpdateAndRecordHistoryAsync(request, cancellationToken, recordHistory: true);
    }

    public static async ValueTask<SourceGenHotDesignApplyResult> ApplyPropertyUpdateAsync(
        SourceGenHotDesignPropertyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.PropertyName))
        {
            return new SourceGenHotDesignApplyResult(false, "PropertyName is required.");
        }

        if (!TryResolveDocument(request.BuildUri, request.TargetType, request.TargetTypeName, out var document))
        {
            return new SourceGenHotDesignApplyResult(false, "Could not resolve hot design document for property update.");
        }

        var resolvedDocument = document!;
        var text = ReadCurrentXamlText(resolvedDocument, XamlSourceGenHotDesignManager.GetStatus().Options.MaxHistoryEntries);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SourceGenHotDesignApplyResult(false, "Target document has no source text available.");
        }

        if (!TryParseXaml(text, out var xamlDocument, out var parseError) || xamlDocument?.Root is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Failed to parse target XAML document: " + parseError);
        }

        var targetElementId = request.ElementId;
        if (string.IsNullOrWhiteSpace(targetElementId))
        {
            lock (Sync)
            {
                if (string.Equals(ActiveBuildUri, resolvedDocument.BuildUri, StringComparison.OrdinalIgnoreCase))
                {
                    targetElementId = SelectedElementId;
                }
            }
        }

        targetElementId ??= "0";
        var targetElement = TryFindElementById(xamlDocument.Root, targetElementId);
        if (targetElement is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Could not locate element '" + targetElementId + "' in the document.");
        }

        var propertyName = request.PropertyName.Trim();
        var attributeName = ResolveAttributeName(targetElement, propertyName);
        if (request.RemoveProperty)
        {
            var existing = targetElement.Attributes(attributeName).FirstOrDefault();
            if (existing is not null)
            {
                existing.Remove();
            }
        }
        else
        {
            targetElement.SetAttributeValue(attributeName, request.PropertyValue ?? string.Empty);
        }

        var serialized = SerializeXaml(xamlDocument);
        var updateRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = serialized,
            PersistChangesToSource = request.PersistChangesToSource,
            WaitForHotReload = request.WaitForHotReload,
            FallbackToRuntimeApplyOnTimeout = request.FallbackToRuntimeApplyOnTimeout
        };

        var result = await ApplyUpdateAndRecordHistoryAsync(updateRequest, cancellationToken, recordHistory: true).ConfigureAwait(false);
        if (result.Succeeded)
        {
            lock (Sync)
            {
                ActiveBuildUri = resolvedDocument.BuildUri;
                SelectedElementId = targetElementId;
            }
        }

        return result;
    }

    public static async ValueTask<SourceGenHotDesignApplyResult> InsertElementAsync(
        SourceGenHotDesignElementInsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ElementName) && string.IsNullOrWhiteSpace(request.XamlFragment))
        {
            return new SourceGenHotDesignApplyResult(false, "ElementName or XamlFragment is required for insert.");
        }

        if (!TryResolveDocument(request.BuildUri, request.TargetType, request.TargetTypeName, out var document))
        {
            return new SourceGenHotDesignApplyResult(false, "Could not resolve hot design document for insert.");
        }

        var resolvedDocument = document!;
        var text = ReadCurrentXamlText(resolvedDocument, XamlSourceGenHotDesignManager.GetStatus().Options.MaxHistoryEntries);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SourceGenHotDesignApplyResult(false, "Target document has no source text available.");
        }

        if (!TryParseXaml(text, out var xamlDocument, out var parseError) || xamlDocument?.Root is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Failed to parse target XAML document: " + parseError);
        }

        var parentId = request.ParentElementId;
        if (string.IsNullOrWhiteSpace(parentId))
        {
            lock (Sync)
            {
                if (string.Equals(ActiveBuildUri, resolvedDocument.BuildUri, StringComparison.OrdinalIgnoreCase))
                {
                    parentId = SelectedElementId;
                }
            }
        }

        parentId ??= "0";
        var parentElement = TryFindElementById(xamlDocument.Root, parentId);
        if (parentElement is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Could not locate parent element '" + parentId + "' for insert.");
        }

        XElement elementToInsert;
        if (!string.IsNullOrWhiteSpace(request.XamlFragment))
        {
            try
            {
                elementToInsert = XElement.Parse(request.XamlFragment!, LoadOptions.SetLineInfo);
            }
            catch (Exception ex)
            {
                return new SourceGenHotDesignApplyResult(false, "Could not parse XAML fragment: " + ex.Message, Error: ex);
            }
        }
        else
        {
            var elementName = request.ElementName.Trim();
            var xName = ResolveElementName(parentElement, elementName);
            elementToInsert = new XElement(xName);
        }

        parentElement.Add(elementToInsert);

        var serialized = SerializeXaml(xamlDocument);
        var updateRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = serialized,
            PersistChangesToSource = request.PersistChangesToSource,
            WaitForHotReload = request.WaitForHotReload,
            FallbackToRuntimeApplyOnTimeout = request.FallbackToRuntimeApplyOnTimeout
        };

        var result = await ApplyUpdateAndRecordHistoryAsync(updateRequest, cancellationToken, recordHistory: true).ConfigureAwait(false);
        if (result.Succeeded)
        {
            lock (Sync)
            {
                ActiveBuildUri = resolvedDocument.BuildUri;
            }
        }

        return result;
    }

    public static async ValueTask<SourceGenHotDesignApplyResult> RemoveElementAsync(
        SourceGenHotDesignElementRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return new SourceGenHotDesignApplyResult(false, "ElementId is required for remove.");
        }

        if (!TryResolveDocument(request.BuildUri, request.TargetType, request.TargetTypeName, out var document))
        {
            return new SourceGenHotDesignApplyResult(false, "Could not resolve hot design document for remove.");
        }

        var resolvedDocument = document!;
        var text = ReadCurrentXamlText(resolvedDocument, XamlSourceGenHotDesignManager.GetStatus().Options.MaxHistoryEntries);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SourceGenHotDesignApplyResult(false, "Target document has no source text available.");
        }

        if (!TryParseXaml(text, out var xamlDocument, out var parseError) || xamlDocument?.Root is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Failed to parse target XAML document: " + parseError);
        }

        var elementId = request.ElementId.Trim();
        if (string.Equals(elementId, "0", StringComparison.Ordinal))
        {
            return new SourceGenHotDesignApplyResult(false, "Cannot remove the root element.");
        }

        var element = TryFindElementById(xamlDocument.Root, elementId);
        if (element is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Could not locate element '" + elementId + "' for remove.");
        }

        element.Remove();

        var serialized = SerializeXaml(xamlDocument);
        var updateRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = serialized,
            PersistChangesToSource = request.PersistChangesToSource,
            WaitForHotReload = request.WaitForHotReload,
            FallbackToRuntimeApplyOnTimeout = request.FallbackToRuntimeApplyOnTimeout
        };

        var result = await ApplyUpdateAndRecordHistoryAsync(updateRequest, cancellationToken, recordHistory: true).ConfigureAwait(false);
        if (result.Succeeded)
        {
            lock (Sync)
            {
                ActiveBuildUri = resolvedDocument.BuildUri;
                SelectedElementId = GetParentElementId(elementId);
            }
        }

        return result;
    }

    public static async ValueTask<SourceGenHotDesignApplyResult> UndoAsync(string? buildUri = null, CancellationToken cancellationToken = default)
    {
        if (!TryResolveUndoRedoDocument(buildUri, out var document))
        {
            return new SourceGenHotDesignApplyResult(false, "No active hot design document is available for undo.");
        }

        var resolvedDocument = document!;
        var maxEntries = XamlSourceGenHotDesignManager.GetStatus().Options.MaxHistoryEntries;
        var current = ReadCurrentXamlText(resolvedDocument, maxEntries);
        if (current is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Target document has no source text available.");
        }

        string previous;
        lock (Sync)
        {
            if (!Histories.TryGetValue(resolvedDocument.BuildUri, out var history) || history.UndoStack.Count == 0)
            {
                return new SourceGenHotDesignApplyResult(false, "Nothing to undo.");
            }

            previous = history.UndoStack[^1];
            history.UndoStack.RemoveAt(history.UndoStack.Count - 1);
        }

        var result = await ApplyUpdateAndRecordHistoryAsync(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = previous
        }, cancellationToken, recordHistory: false).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            lock (Sync)
            {
                if (Histories.TryGetValue(resolvedDocument.BuildUri, out var history))
                {
                    history.UndoStack.Add(previous);
                }
            }

            return result;
        }

        lock (Sync)
        {
            if (!Histories.TryGetValue(resolvedDocument.BuildUri, out var history))
            {
                history = new DocumentHistoryState(resolvedDocument.BuildUri, current);
                Histories[resolvedDocument.BuildUri] = history;
            }

            history.RedoStack.Add(current);
            TrimStack(history.RedoStack, maxEntries);
            history.CurrentXaml = previous;
            ActiveBuildUri = resolvedDocument.BuildUri;
        }

        return result with { Message = "Undo applied." };
    }

    public static async ValueTask<SourceGenHotDesignApplyResult> RedoAsync(string? buildUri = null, CancellationToken cancellationToken = default)
    {
        if (!TryResolveUndoRedoDocument(buildUri, out var document))
        {
            return new SourceGenHotDesignApplyResult(false, "No active hot design document is available for redo.");
        }

        var resolvedDocument = document!;
        var maxEntries = XamlSourceGenHotDesignManager.GetStatus().Options.MaxHistoryEntries;
        var current = ReadCurrentXamlText(resolvedDocument, maxEntries);
        if (current is null)
        {
            return new SourceGenHotDesignApplyResult(false, "Target document has no source text available.");
        }

        string next;
        lock (Sync)
        {
            if (!Histories.TryGetValue(resolvedDocument.BuildUri, out var history) || history.RedoStack.Count == 0)
            {
                return new SourceGenHotDesignApplyResult(false, "Nothing to redo.");
            }

            next = history.RedoStack[^1];
            history.RedoStack.RemoveAt(history.RedoStack.Count - 1);
        }

        var result = await ApplyUpdateAndRecordHistoryAsync(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = next
        }, cancellationToken, recordHistory: false).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            lock (Sync)
            {
                if (Histories.TryGetValue(resolvedDocument.BuildUri, out var history))
                {
                    history.RedoStack.Add(next);
                }
            }

            return result;
        }

        lock (Sync)
        {
            if (!Histories.TryGetValue(resolvedDocument.BuildUri, out var history))
            {
                history = new DocumentHistoryState(resolvedDocument.BuildUri, current);
                Histories[resolvedDocument.BuildUri] = history;
            }

            history.UndoStack.Add(current);
            TrimStack(history.UndoStack, maxEntries);
            history.CurrentXaml = next;
            ActiveBuildUri = resolvedDocument.BuildUri;
        }

        return result with { Message = "Redo applied." };
    }

    private static bool TryResolveUndoRedoDocument(string? buildUri, out SourceGenHotDesignDocumentDescriptor? document)
    {
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        string? targetBuildUri;
        lock (Sync)
        {
            targetBuildUri = string.IsNullOrWhiteSpace(buildUri) ? ActiveBuildUri : buildUri.Trim();
            if (targetBuildUri is null && documents.Count > 0)
            {
                targetBuildUri = SelectDefaultDocument(documents)?.BuildUri;
                ActiveBuildUri = targetBuildUri;
            }
        }

        var resolved = ResolveDocument(documents, targetBuildUri, null, null);
        if (resolved is null)
        {
            document = default;
            return false;
        }

        document = resolved;
        return true;
    }

    private static async ValueTask<SourceGenHotDesignApplyResult> ApplyUpdateAndRecordHistoryAsync(
        SourceGenHotDesignUpdateRequest request,
        CancellationToken cancellationToken,
        bool recordHistory)
    {
        SourceGenHotDesignDocumentDescriptor? document = null;
        string? previousText = null;
        var maxEntries = XamlSourceGenHotDesignManager.GetStatus().Options.MaxHistoryEntries;

        if (recordHistory)
        {
            var docs = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
            document = ResolveDocument(docs, request.BuildUri, request.TargetType, request.TargetTypeName);
            if (document is not null)
            {
                previousText = ReadCurrentXamlText(document, maxEntries);
            }
        }

        SourceGenHotDesignApplyResult result;
        if (XamlSourceGenStudioManager.IsEnabled)
        {
            var studioResult = await XamlSourceGenStudioManager.ApplyUpdateAsync(
                new SourceGenStudioUpdateRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    CorrelationId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    BuildUri = request.BuildUri,
                    TargetType = request.TargetType,
                    TargetTypeName = request.TargetTypeName,
                    XamlText = request.XamlText,
                    PersistChangesToSource = request.PersistChangesToSource,
                    WaitMode = request.WaitForHotReload.HasValue
                        ? (request.WaitForHotReload.Value
                            ? SourceGenStudioWaitMode.WaitForLocalOnly
                            : SourceGenStudioWaitMode.None)
                        : null,
                    FallbackPolicy = request.FallbackToRuntimeApplyOnTimeout.HasValue
                        ? (request.FallbackToRuntimeApplyOnTimeout.Value
                            ? SourceGenStudioFallbackPolicy.RuntimeApplyOnTimeout
                            : SourceGenStudioFallbackPolicy.NoFallback)
                        : null
                },
                cancellationToken).ConfigureAwait(false);

            result = new SourceGenHotDesignApplyResult(
                Succeeded: studioResult.Succeeded,
                Message: studioResult.Message,
                BuildUri: studioResult.BuildUri,
                TargetType: studioResult.TargetType,
                SourcePersisted: studioResult.SourcePersisted,
                HotReloadObserved: studioResult.LocalUpdateObserved,
                RuntimeFallbackApplied: studioResult.RuntimeFallbackApplied,
                Error: studioResult.Error);
        }
        else
        {
            result = await XamlSourceGenHotDesignManager.ApplyUpdateAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (!result.Succeeded || !recordHistory || document is null)
        {
            return result;
        }

        var newText = request.XamlText;
        lock (Sync)
        {
            if (!Histories.TryGetValue(document.BuildUri, out var history))
            {
                history = new DocumentHistoryState(document.BuildUri, previousText ?? newText);
                Histories[document.BuildUri] = history;
            }

            if (!string.Equals(history.CurrentXaml, newText, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(history.CurrentXaml))
                {
                    history.UndoStack.Add(history.CurrentXaml);
                    TrimStack(history.UndoStack, maxEntries);
                }

                history.RedoStack.Clear();
                history.CurrentXaml = newText;
                ActiveBuildUri = document.BuildUri;
            }
        }

        return result;
    }

    private static string? ReadCurrentXamlText(SourceGenHotDesignDocumentDescriptor document, int maxHistoryEntries)
    {
        var normalizedMax = Math.Max(1, maxHistoryEntries);

        lock (Sync)
        {
            if (Histories.TryGetValue(document.BuildUri, out var history))
            {
                return history.CurrentXaml;
            }
        }

        var sourcePath = document.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(sourcePath);
        }
        catch
        {
            return null;
        }

        lock (Sync)
        {
            Histories[document.BuildUri] = new DocumentHistoryState(document.BuildUri, text)
            {
                MaxEntries = normalizedMax
            };
        }

        return text;
    }

    private static IReadOnlyList<SourceGenHotDesignElementNode> BuildElementTree(
        string? xamlText,
        string? selectedElementId,
        string? search,
        out bool selectionExists)
    {
        selectionExists = false;
        if (string.IsNullOrWhiteSpace(xamlText))
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        if (!TryParseXaml(xamlText, out var document, out _) || document?.Root is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var node = BuildElementNode(document.Root, "0", 0, selectedElementId, query, ref selectionExists);
        if (node is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        return [node];
    }

    private static SourceGenHotDesignElementNode? BuildElementNode(
        XElement element,
        string elementId,
        int depth,
        string? selectedElementId,
        string? search,
        ref bool selectionExists)
    {
        var children = new List<SourceGenHotDesignElementNode>();
        var index = 0;
        foreach (var child in element.Elements())
        {
            var childId = elementId + "/" + index;
            var childNode = BuildElementNode(child, childId, depth + 1, selectedElementId, search, ref selectionExists);
            if (childNode is not null)
            {
                children.Add(childNode);
            }

            index++;
        }

        var typeName = element.Name.LocalName;
        var xamlName = TryGetName(element);
        var classes = TryGetAttributeValue(element, "Classes");
        var isSelected = string.Equals(elementId, selectedElementId, StringComparison.Ordinal);
        if (isSelected)
        {
            selectionExists = true;
        }

        var lineInfo = (IXmlLineInfo)element;
        var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
        var displayName = xamlName is null
            ? "[" + typeName + "]"
            : "[" + typeName + "] " + xamlName;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchText = search!;
            var matches = displayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                          || typeName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                          || (xamlName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!matches && children.Count == 0)
            {
                return null;
            }
        }

        return new SourceGenHotDesignElementNode(
            Id: elementId,
            DisplayName: displayName,
            TypeName: typeName,
            XamlName: xamlName,
            Classes: classes,
            Depth: depth,
            IsSelected: isSelected,
            Line: line,
            Children: children);
    }

    private static IReadOnlyList<SourceGenHotDesignPropertyEntry> BuildPropertyEntries(
        string? xamlText,
        string? selectedElementId,
        SourceGenHotDesignPropertyFilterMode mode)
    {
        if (string.IsNullOrWhiteSpace(xamlText))
        {
            return Array.Empty<SourceGenHotDesignPropertyEntry>();
        }

        if (!TryParseXaml(xamlText, out var document, out _) || document?.Root is null)
        {
            return Array.Empty<SourceGenHotDesignPropertyEntry>();
        }

        var targetElement = TryFindElementById(document.Root, string.IsNullOrWhiteSpace(selectedElementId) ? "0" : selectedElementId!);
        targetElement ??= document.Root;

        var properties = new Dictionary<string, SourceGenHotDesignPropertyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in targetElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var name = FormatAttributeName(targetElement, attribute.Name);
            var value = attribute.Value;
            var isMarkup = value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal);

            properties[name] = new SourceGenHotDesignPropertyEntry(
                Name: name,
                Value: value,
                TypeName: "string",
                IsSet: true,
                IsAttached: name.Contains('.', StringComparison.Ordinal),
                IsMarkupExtension: isMarkup,
                QuickSets: GetQuickSets(name));
        }

        if (mode == SourceGenHotDesignPropertyFilterMode.All)
        {
            var resolvedType = ResolveElementType(targetElement);
            if (resolvedType is not null)
            {
                foreach (var avaloniaProperty in EnumerateAvaloniaProperties(resolvedType))
                {
                    if (properties.ContainsKey(avaloniaProperty.Name))
                    {
                        continue;
                    }

                    properties[avaloniaProperty.Name] = new SourceGenHotDesignPropertyEntry(
                        Name: avaloniaProperty.Name,
                        Value: null,
                        TypeName: avaloniaProperty.PropertyType.Name,
                        IsSet: false,
                        IsAttached: avaloniaProperty.IsAttached,
                        IsMarkupExtension: false,
                        QuickSets: GetQuickSets(avaloniaProperty.Name));
                }
            }
        }

        return properties.Values
            .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SourceGenHotDesignToolboxCategory> BuildToolboxCategories(string? search)
    {
        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var allItems = new List<SourceGenHotDesignToolboxItem>
        {
            new("TextBlock", "TextBlock", "Common", "<TextBlock Text=\"Text\" />", false),
            new("TextBox", "TextBox", "Common", "<TextBox Text=\"\" />", false),
            new("Button", "Button", "Common", "<Button Content=\"Button\" />", false),
            new("CheckBox", "CheckBox", "Common", "<CheckBox Content=\"Check\" />", false),
            new("ComboBox", "ComboBox", "Common", "<ComboBox />", false),
            new("Slider", "Slider", "Common", "<Slider Minimum=\"0\" Maximum=\"100\" />", false),
            new("ProgressBar", "ProgressBar", "Common", "<ProgressBar Minimum=\"0\" Maximum=\"100\" Value=\"40\" />", false),
            new("StackPanel", "StackPanel", "Layout", "<StackPanel Spacing=\"8\" />", false),
            new("Grid", "Grid", "Layout", "<Grid RowDefinitions=\"Auto,*\" ColumnDefinitions=\"*\" />", false),
            new("DockPanel", "DockPanel", "Layout", "<DockPanel />", false),
            new("WrapPanel", "WrapPanel", "Layout", "<WrapPanel />", false),
            new("Canvas", "Canvas", "Layout", "<Canvas />", false),
            new("Border", "Border", "Layout", "<Border Padding=\"8\" />", false),
            new("ScrollViewer", "ScrollViewer", "Navigation", "<ScrollViewer />", false),
            new("TabControl", "TabControl", "Navigation", "<TabControl />", false),
            new("Expander", "Expander", "Navigation", "<Expander Header=\"Section\" />", false),
            new("ListBox", "ListBox", "Collections", "<ListBox />", false),
            new("ItemsControl", "ItemsControl", "Collections", "<ItemsControl />", false),
            new("TreeView", "TreeView", "Collections", "<TreeView />", false),
            new("DataGrid", "DataGrid", "Collections", "<DataGrid />", false)
        };

        var projectControlTypes = new HashSet<Type>();
        var projectAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in XamlSourceGenHotDesignManager.GetRegisteredDocuments())
        {
            AddProjectControlType(projectControlTypes, document.RootType);
            var assemblyName = document.RootType.Assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                projectAssemblyNames.Add(assemblyName);
            }
        }

        var registeredTypes = SourceGenKnownTypeRegistry.GetRegisteredTypes();
        for (var index = 0; index < registeredTypes.Count; index++)
        {
            var candidate = registeredTypes[index];
            var candidateAssemblyName = candidate.Assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(candidateAssemblyName) ||
                !projectAssemblyNames.Contains(candidateAssemblyName))
            {
                continue;
            }

            AddProjectControlType(projectControlTypes, candidate);
        }

        foreach (var projectType in projectControlTypes.OrderBy(static type => type.Name, StringComparer.OrdinalIgnoreCase))
        {
            allItems.Add(new SourceGenHotDesignToolboxItem(
                Name: projectType.Name,
                DisplayName: projectType.Name,
                Category: "Project",
                XamlSnippet: "<" + projectType.Name + " />",
                IsProjectControl: true));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            allItems = allItems
                .Where(item =>
                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return allItems
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SourceGenHotDesignToolboxCategory(
                group.Key,
                group
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    private static void AddProjectControlType(ISet<Type> collector, Type type)
    {
        if (!typeof(Control).IsAssignableFrom(type) ||
            type.IsAbstract ||
            !type.IsPublic)
        {
            return;
        }

        if (type.Namespace is not null &&
            type.Namespace.StartsWith("Avalonia.", StringComparison.Ordinal))
        {
            return;
        }

        collector.Add(type);
    }

    private static bool TryResolveDocument(string? buildUri, Type? targetType, string? targetTypeName, out SourceGenHotDesignDocumentDescriptor? document)
    {
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        var resolved = ResolveDocument(documents, buildUri, targetType, targetTypeName);
        if (resolved is null)
        {
            document = default;
            return false;
        }

        document = resolved;
        return true;
    }

    private static SourceGenHotDesignDocumentDescriptor? ResolveDocument(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents,
        string? buildUri,
        Type? targetType,
        string? targetTypeName)
    {
        if (!string.IsNullOrWhiteSpace(buildUri))
        {
            var normalizedBuildUri = buildUri.Trim();
            var byBuildUri = documents.FirstOrDefault(document =>
                string.Equals(document.BuildUri, normalizedBuildUri, StringComparison.OrdinalIgnoreCase));
            if (byBuildUri is not null)
            {
                return byBuildUri;
            }
        }

        if (targetType is not null)
        {
            var byType = documents.FirstOrDefault(document => NormalizeType(document.RootType) == NormalizeType(targetType));
            if (byType is not null)
            {
                return byType;
            }
        }

        if (!string.IsNullOrWhiteSpace(targetTypeName))
        {
            var normalizedName = targetTypeName.Trim();
            var byTypeName = documents.FirstOrDefault(document =>
                string.Equals(document.RootType.FullName, normalizedName, StringComparison.Ordinal) ||
                string.Equals(document.RootType.Name, normalizedName, StringComparison.Ordinal));
            if (byTypeName is not null)
            {
                return byTypeName;
            }
        }

        return SelectDefaultDocument(documents);
    }

    private static Type NormalizeType(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }

    private static SourceGenHotDesignDocumentDescriptor? SelectDefaultDocument(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        if (documents.Count == 0)
        {
            return null;
        }

        return documents
            .OrderBy(GetDefaultDocumentScore)
            .FirstOrDefault();
    }

    private static int GetDefaultDocumentScore(SourceGenHotDesignDocumentDescriptor document)
    {
        var score = document.ArtifactKind switch
        {
            SourceGenHotDesignArtifactKind.View => 0,
            SourceGenHotDesignArtifactKind.Unknown when document.DocumentRole == SourceGenHotDesignDocumentRole.Root => 1,
            _ => 4
        };

        if (document.DocumentRole is SourceGenHotDesignDocumentRole.Theme or
            SourceGenHotDesignDocumentRole.Resources or
            SourceGenHotDesignDocumentRole.Template)
        {
            score += 3;
        }

        if (document.ArtifactKind == SourceGenHotDesignArtifactKind.Application ||
            typeof(global::Avalonia.Application).IsAssignableFrom(document.RootType))
        {
            score += 6;
        }

        if (document.LiveInstanceCount <= 0)
        {
            score += 1;
        }

        return score;
    }

    private static bool TryParseXaml(string text, out XDocument? document, out string? error)
    {
        try
        {
            document = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    private static string SerializeXaml(XDocument document)
    {
        return document.ToString(SaveOptions.None);
    }

    private static XElement? TryFindElementById(XElement root, string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var segments = elementId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments[0] != "0")
        {
            return null;
        }

        var current = root;
        for (var segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
        {
            if (!int.TryParse(segments[segmentIndex], out var childIndex) || childIndex < 0)
            {
                return null;
            }

            var children = current.Elements().ToArray();
            if (childIndex >= children.Length)
            {
                return null;
            }

            current = children[childIndex];
        }

        return current;
    }

    private static string? SelectPreferredElementId(
        IReadOnlyList<SourceGenHotDesignElementNode> roots)
    {
        if (roots.Count == 0)
        {
            return null;
        }

        var root = roots[0];
        var preferredChild = FindPreferredDescendant(root);
        return preferredChild?.Id ?? root.Id;
    }

    private static SourceGenHotDesignElementNode? FindPreferredDescendant(
        SourceGenHotDesignElementNode node)
    {
        if (node.Children.Count == 0)
        {
            return null;
        }

        var firstChild = node.Children[0];
        return FindPreferredDescendant(firstChild) ?? firstChild;
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

    private static bool TryFindMatchingElementByName(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        IReadOnlyList<string> controlNames,
        out SourceGenHotDesignElementNode? matched)
    {
        matched = null;
        for (var nameIndex = 0; nameIndex < controlNames.Count; nameIndex++)
        {
            var controlName = controlNames[nameIndex];
            if (string.IsNullOrWhiteSpace(controlName))
            {
                continue;
            }

            SourceGenHotDesignElementNode? candidate = null;
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                if (!string.Equals(node.XamlName, controlName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (candidate is null || node.Depth > candidate.Depth)
                {
                    candidate = node;
                }
            }

            if (candidate is null)
            {
                continue;
            }

            matched = candidate;
            return true;
        }

        return false;
    }

    private static bool TryFindMatchingElementByType(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        IReadOnlyList<string> controlTypeNames,
        out SourceGenHotDesignElementNode? matched)
    {
        matched = null;
        for (var typeIndex = 0; typeIndex < controlTypeNames.Count; typeIndex++)
        {
            var controlTypeName = controlTypeNames[typeIndex];
            if (string.IsNullOrWhiteSpace(controlTypeName))
            {
                continue;
            }

            SourceGenHotDesignElementNode? candidate = null;
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                if (!string.Equals(node.TypeName, controlTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (candidate is null || node.Depth > candidate.Depth)
                {
                    candidate = node;
                }
            }

            if (candidate is null)
            {
                continue;
            }

            matched = candidate;
            return true;
        }

        return false;
    }

    private static string? TryGetName(XElement element)
    {
        var name = TryGetAttributeValue(element, "Name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var xName = element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal) &&
            attribute.Name.NamespaceName.Contains("schemas.microsoft.com/winfx/2006/xaml", StringComparison.OrdinalIgnoreCase));
        return xName?.Value;
    }

    private static string? TryGetAttributeValue(XElement element, string localName)
    {
        return element.Attributes()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase) &&
                !attribute.IsNamespaceDeclaration)
            ?.Value;
    }

    private static XName ResolveAttributeName(XElement element, string propertyName)
    {
        if (propertyName.Contains(':', StringComparison.Ordinal))
        {
            var parts = propertyName.Split(':', 2);
            var prefix = parts[0];
            var localName = parts[1];
            var ns = element.GetNamespaceOfPrefix(prefix);
            return ns is null ? XName.Get(propertyName) : ns + localName;
        }

        return XName.Get(propertyName);
    }

    private static XName ResolveElementName(XElement parent, string elementName)
    {
        if (elementName.Contains(':', StringComparison.Ordinal))
        {
            var parts = elementName.Split(':', 2);
            var prefix = parts[0];
            var localName = parts[1];
            var ns = parent.GetNamespaceOfPrefix(prefix);
            return ns is null ? XName.Get(elementName) : ns + localName;
        }

        var defaultNamespace = parent.GetDefaultNamespace();
        if (defaultNamespace == XNamespace.None)
        {
            return XName.Get(elementName);
        }

        return defaultNamespace + elementName;
    }

    private static string GetParentElementId(string elementId)
    {
        var slashIndex = elementId.LastIndexOf("/", StringComparison.Ordinal);
        if (slashIndex <= 0)
        {
            return "0";
        }

        return elementId.Substring(0, slashIndex);
    }

    private static string FormatAttributeName(XElement element, XName attributeName)
    {
        if (attributeName.Namespace == XNamespace.None)
        {
            return attributeName.LocalName;
        }

        var prefix = element.GetPrefixOfNamespace(attributeName.Namespace);
        return string.IsNullOrWhiteSpace(prefix)
            ? attributeName.LocalName
            : prefix + ":" + attributeName.LocalName;
    }

    private static Type? ResolveElementType(XElement element)
    {
        var namespaceUri = element.Name.NamespaceName;
        var localName = element.Name.LocalName;
        var cacheKey = namespaceUri + "|" + localName;

        lock (Sync)
        {
            if (TypeResolutionCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var type = ResolveElementTypeCore(namespaceUri, localName);
        lock (Sync)
        {
            TypeResolutionCache[cacheKey] = type;
        }

        return type;
    }

    private static Type? ResolveElementTypeCore(string namespaceUri, string localName)
    {
        if (string.IsNullOrWhiteSpace(namespaceUri) ||
            string.IsNullOrWhiteSpace(localName))
        {
            return null;
        }

        return SourceGenKnownTypeRegistry.TryResolve(namespaceUri, localName, out var type)
            ? type
            : null;
    }

    private static IReadOnlyList<AvaloniaPropertyDescriptor> EnumerateAvaloniaProperties(Type type)
    {
        var descriptors = new Dictionary<string, AvaloniaPropertyDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(type))
        {
            AddAvaloniaPropertyDescriptor(descriptors, property);
        }

        foreach (var attachedProperty in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(type))
        {
            AddAvaloniaPropertyDescriptor(descriptors, attachedProperty);
        }

        return descriptors.Values
            .OrderBy(static descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddAvaloniaPropertyDescriptor(
        IDictionary<string, AvaloniaPropertyDescriptor> descriptors,
        AvaloniaProperty property)
    {
        var propertyName = property.Name;
        if (string.IsNullOrWhiteSpace(propertyName) ||
            descriptors.ContainsKey(propertyName))
        {
            return;
        }

        descriptors[propertyName] = new AvaloniaPropertyDescriptor(
            propertyName,
            property.PropertyType,
            property.IsAttached);
    }

    private static IReadOnlyList<SourceGenHotDesignPropertyQuickSet> GetQuickSets(string propertyName)
    {
        if (string.Equals(propertyName, "HorizontalAlignment", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Left", "Left"),
                new("Center", "Center"),
                new("Right", "Right"),
                new("Stretch", "Stretch")
            ];
        }

        if (string.Equals(propertyName, "VerticalAlignment", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Top", "Top"),
                new("Center", "Center"),
                new("Bottom", "Bottom"),
                new("Stretch", "Stretch")
            ];
        }

        if (string.Equals(propertyName, "Orientation", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Horizontal", "Horizontal"),
                new("Vertical", "Vertical")
            ];
        }

        if (string.Equals(propertyName, "FontWeight", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Normal", "Normal"),
                new("Bold", "Bold"),
                new("SemiBold", "SemiBold")
            ];
        }

        if (string.Equals(propertyName, "TextAlignment", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Left", "Left"),
                new("Center", "Center"),
                new("Right", "Right"),
                new("Justify", "Justify")
            ];
        }

        if (string.Equals(propertyName, "DockPanel.Dock", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Top", "Top"),
                new("Right", "Right"),
                new("Bottom", "Bottom"),
                new("Left", "Left")
            ];
        }

        return Array.Empty<SourceGenHotDesignPropertyQuickSet>();
    }

    private static void TrimStack(List<string> stack, int maxEntries)
    {
        var normalizedMax = Math.Max(1, maxEntries);
        while (stack.Count > normalizedMax)
        {
            stack.RemoveAt(0);
        }
    }

    private sealed class DocumentHistoryState(string buildUri, string currentXaml)
    {
        public string BuildUri { get; } = buildUri;

        public string CurrentXaml { get; set; } = currentXaml;

        public int MaxEntries { get; set; } = 40;

        public List<string> UndoStack { get; } = [];

        public List<string> RedoStack { get; } = [];
    }

    private sealed record AvaloniaPropertyDescriptor(
        string Name,
        Type PropertyType,
        bool IsAttached);
}
