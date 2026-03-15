using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotDesignCoreTools
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DocumentHistoryState> Histories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type?> TypeResolutionCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LayoutPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Width",
        "Height",
        "MinWidth",
        "MinHeight",
        "MaxWidth",
        "MaxHeight",
        "Margin",
        "Padding",
        "HorizontalAlignment",
        "VerticalAlignment",
        "HorizontalContentAlignment",
        "VerticalContentAlignment",
        "Grid.Row",
        "Grid.Column",
        "Grid.RowSpan",
        "Grid.ColumnSpan",
        "Canvas.Left",
        "Canvas.Top",
        "Canvas.Right",
        "Canvas.Bottom",
        "DockPanel.Dock"
    };
    private static readonly HashSet<string> AppearancePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Background",
        "Foreground",
        "BorderBrush",
        "BorderThickness",
        "CornerRadius",
        "Opacity",
        "FontFamily",
        "FontSize",
        "FontStyle",
        "FontWeight",
        "Classes"
    };
    private static readonly HashSet<string> DataPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataContext",
        "ItemsSource",
        "SelectedItem",
        "SelectedIndex",
        "SelectedValue",
        "Tag"
    };
    private static readonly HashSet<string> InteractionPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Command",
        "CommandParameter",
        "IsEnabled",
        "IsVisible",
        "IsHitTestVisible",
        "Focusable"
    };

    private static SourceGenHotDesignWorkspaceMode WorkspaceMode = SourceGenHotDesignWorkspaceMode.Design;
    private static SourceGenHotDesignPropertyFilterMode PropertyFilterMode = SourceGenHotDesignPropertyFilterMode.Smart;
    private static SourceGenHotDesignHitTestMode HitTestMode = SourceGenHotDesignHitTestMode.Logical;
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
            HitTestMode = SourceGenHotDesignHitTestMode.Logical;
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
        if (selectedDocument is not null &&
            !string.Equals(currentActiveBuildUri, selectedDocument.BuildUri, StringComparison.OrdinalIgnoreCase))
        {
            currentActiveBuildUri = selectedDocument.BuildUri;
            lock (Sync)
            {
                ActiveBuildUri = currentActiveBuildUri;
            }
        }

        var currentText = TryReadCurrentXamlDocument(
            selectedDocument,
            status.Options.MaxHistoryEntries,
            out var currentDocument,
            out var currentXamlText)
            ? currentXamlText
            : selectedDocument is null
                ? null
                : ReadCurrentXamlText(selectedDocument, status.Options.MaxHistoryEntries);

        var elements = BuildElementTree(
            currentDocument,
            currentSelectedElementId,
            search,
            currentActiveBuildUri,
            out var selectionExists);
        if (!selectionExists)
        {
            currentSelectedElementId = SelectPreferredElementId(elements);
            lock (Sync)
            {
                SelectedElementId = currentSelectedElementId;
            }
        }

        var properties = BuildPropertyEntries(currentDocument, currentSelectedElementId, currentPropertyFilterMode);

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
            Toolbox: BuildToolboxCategories(documents, search));
    }

    public static bool TryResolveElementForLiveSelection(
        IReadOnlyList<string>? controlNames,
        IReadOnlyList<string>? controlTypeNames,
        out string? buildUri,
        out string? elementId)
    {
        return TryResolveElementForLiveSelection(
            controlNames,
            controlTypeNames,
            preferredBuildUri: null,
            allowAmbiguousTypeFallback: true,
            out buildUri,
            out elementId);
    }

    public static bool TryResolveElementForLiveSelection(
        IReadOnlyList<string>? controlNames,
        IReadOnlyList<string>? controlTypeNames,
        string? preferredBuildUri,
        bool allowAmbiguousTypeFallback,
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

        var orderedDocuments = OrderDocumentsForLiveSelection(documents, preferredBuildUri);
        var typeCandidates = new List<ResolvedElementCandidate>(orderedDocuments.Count);
        for (var documentIndex = 0; documentIndex < orderedDocuments.Count; documentIndex++)
        {
            var document = orderedDocuments[documentIndex];
            var text = ReadCurrentXamlText(document, status.Options.MaxHistoryEntries);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var elements = BuildElementTree(
                text,
                selectedElementId: null,
                search: null,
                document.BuildUri,
                out _);
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
                typeCandidates.Add(new ResolvedElementCandidate(document.BuildUri, matchedByType!.Id));
            }
        }

        if (typeCandidates.Count == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(preferredBuildUri))
        {
            var normalizedPreferredBuildUri = preferredBuildUri.Trim();
            for (var index = 0; index < typeCandidates.Count; index++)
            {
                var candidate = typeCandidates[index];
                if (!string.Equals(candidate.BuildUri, normalizedPreferredBuildUri, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                buildUri = candidate.BuildUri;
                elementId = candidate.ElementId;
                return true;
            }
        }

        if (!allowAmbiguousTypeFallback && IsAmbiguousTypeMatch(typeCandidates))
        {
            return false;
        }

        var selectedCandidate = typeCandidates[0];
        buildUri = selectedCandidate.BuildUri;
        elementId = selectedCandidate.ElementId;
        return true;
    }

    public static bool TryGetCurrentDocumentText(string buildUri, out string xamlText)
    {
        xamlText = string.Empty;
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            return false;
        }

        var status = XamlSourceGenHotDesignManager.GetStatus();
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        var document = FindDocumentByBuildUri(documents, buildUri);
        if (document is null)
        {
            return false;
        }

        var text = ReadCurrentXamlText(document, status.Options.MaxHistoryEntries);
        if (text is null)
        {
            return false;
        }

        xamlText = text;
        return true;
    }

    internal static bool TryBuildElementTreeForDocument(
        string buildUri,
        out IReadOnlyList<SourceGenHotDesignElementNode> elements)
    {
        elements = Array.Empty<SourceGenHotDesignElementNode>();
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            return false;
        }

        var status = XamlSourceGenHotDesignManager.GetStatus();
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        var document = FindDocumentByBuildUri(documents, buildUri);
        if (!TryReadCurrentXamlDocument(document, status.Options.MaxHistoryEntries, out var xamlDocument, out _))
        {
            return false;
        }

        elements = BuildElementTree(
            xamlDocument,
            selectedElementId: null,
            search: null,
            buildUri,
            out _);
        return elements.Count > 0;
    }

    internal static IReadOnlyList<SourceGenHotDesignPropertyEntry> BuildRuntimePropertyEntries(
        AvaloniaObject target,
        SourceGenHotDesignPropertyFilterMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);

        var descriptors = EnumerateAvaloniaProperties(target.GetType());
        if (descriptors.Count == 0)
        {
            return Array.Empty<SourceGenHotDesignPropertyEntry>();
        }

        var properties = new List<SourceGenHotDesignPropertyEntry>(descriptors.Count);
        for (var index = 0; index < descriptors.Count; index++)
        {
            var descriptor = descriptors[index];
            global::Avalonia.Diagnostics.AvaloniaPropertyValue diagnostic;
            try
            {
                diagnostic = global::Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(target, descriptor.Property);
            }
            catch
            {
                continue;
            }

            var isSet = target.IsSet(descriptor.Property) ||
                        diagnostic.Priority != global::Avalonia.Data.BindingPriority.Unset;
            if (mode == SourceGenHotDesignPropertyFilterMode.Smart &&
                !ShouldIncludeRuntimePropertyInSmartFilter(descriptor, isSet))
            {
                continue;
            }

            var value = diagnostic.Value;
            properties.Add(new SourceGenHotDesignPropertyEntry(
                Name: descriptor.Name,
                Value: FormatRuntimePropertyValue(value),
                TypeName: descriptor.PropertyType.Name,
                IsSet: isSet,
                IsAttached: descriptor.IsAttached,
                IsMarkupExtension: false,
                QuickSets: GetQuickSets(descriptor.Name, descriptor.PropertyType),
                Category: ClassifyPropertyCategory(descriptor.Name, descriptor.PropertyType, descriptor.IsAttached),
                Source: GetRuntimePropertySource(diagnostic.Priority, target.IsSet(descriptor.Property)),
                OwnerTypeName: descriptor.OwnerType.Name,
                EditorKind: GetEditorKind(descriptor.PropertyType, isMarkupExtension: false),
                IsReadOnly: descriptor.IsReadOnly,
                CanReset: !descriptor.IsReadOnly && target.IsSet(descriptor.Property),
                EnumOptions: GetEnumOptions(descriptor.PropertyType)));
        }

        return properties
            .OrderByDescending(static property => property.IsPinned)
            .ThenByDescending(static property => property.IsSet)
            .ThenBy(static property => property.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    public static SourceGenHotDesignHitTestMode GetHitTestMode()
    {
        lock (Sync)
        {
            return HitTestMode;
        }
    }

    public static void SetHitTestMode(SourceGenHotDesignHitTestMode mode)
    {
        lock (Sync)
        {
            HitTestMode = mode;
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
        ThrowIfNullOrWhiteSpace(formFactor, nameof(formFactor));

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
        ThrowIfNullOrWhiteSpace(buildUri, nameof(buildUri));
        ArgumentNullException.ThrowIfNull(xamlText);

        var request = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = buildUri,
            XamlText = xamlText
        };

        return ApplyUpdateAndRecordHistoryAsync(request, cancellationToken, recordHistory: true);
    }

    private static void ThrowIfNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
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

        if (!XamlSourceGenHotDesignDocumentEditor.TryCreate(text, out var editor, out var editorError) || editor is null)
        {
            return new SourceGenHotDesignApplyResult(false, editorError ?? "Failed to parse target XAML document.");
        }

        if (!editor.TryApplyPropertyUpdate(
                targetElementId,
                request.PropertyName.Trim(),
                request.PropertyValue,
                request.RemoveProperty,
                out var updatedText,
                out var updateError))
        {
            return new SourceGenHotDesignApplyResult(false, updateError ?? "Failed to apply property update.");
        }

        if (string.Equals(updatedText, text, StringComparison.Ordinal))
        {
            lock (Sync)
            {
                ActiveBuildUri = resolvedDocument.BuildUri;
                SelectedElementId = targetElementId;
            }

            return new SourceGenHotDesignApplyResult(
                true,
                request.RemoveProperty ? "Property was already removed." : "Property is already up to date.",
                BuildUri: resolvedDocument.BuildUri,
                SourcePersisted: false);
        }

        var updateRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = updatedText,
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

        if (!XamlSourceGenHotDesignDocumentEditor.TryCreate(text, out var editor, out var editorError) || editor is null)
        {
            return new SourceGenHotDesignApplyResult(false, editorError ?? "Failed to parse target XAML document.");
        }

        if (!editor.TryInsertElement(
                parentId,
                request.ElementName,
                request.XamlFragment,
                out var updatedText,
                out var updateError))
        {
            return new SourceGenHotDesignApplyResult(false, updateError ?? "Failed to insert element.");
        }

        if (string.Equals(updatedText, text, StringComparison.Ordinal))
        {
            lock (Sync)
            {
                ActiveBuildUri = resolvedDocument.BuildUri;
            }

            return new SourceGenHotDesignApplyResult(
                true,
                "Element insert produced no changes.",
                BuildUri: resolvedDocument.BuildUri,
                SourcePersisted: false);
        }
        var updateRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = updatedText,
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

        var elementId = request.ElementId.Trim();
        if (string.Equals(elementId, "0", StringComparison.Ordinal))
        {
            return new SourceGenHotDesignApplyResult(false, "Cannot remove the root element.");
        }

        if (!XamlSourceGenHotDesignDocumentEditor.TryCreate(text, out var editor, out var editorError) || editor is null)
        {
            return new SourceGenHotDesignApplyResult(false, editorError ?? "Failed to parse target XAML document.");
        }

        if (!editor.TryRemoveElement(elementId, out var updatedText, out var updateError))
        {
            return new SourceGenHotDesignApplyResult(false, updateError ?? "Failed to remove element.");
        }

        var updateRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = resolvedDocument.BuildUri,
            XamlText = updatedText,
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
        XDocument? xamlDocument,
        string? selectedElementId,
        string? search,
        string? sourceBuildUri,
        out bool selectionExists)
    {
        selectionExists = false;
        if (xamlDocument?.Root is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var node = BuildElementNode(
            xamlDocument.Root,
            "0",
            0,
            selectedElementId,
            query,
            sourceBuildUri,
            ref selectionExists);
        if (node is null)
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        return [node];
    }

    private static IReadOnlyList<SourceGenHotDesignElementNode> BuildElementTree(
        string? xamlText,
        string? selectedElementId,
        string? search,
        string? sourceBuildUri,
        out bool selectionExists)
    {
        selectionExists = false;
        if (!TryParseXamlDocument(xamlText, out var xamlDocument))
        {
            return Array.Empty<SourceGenHotDesignElementNode>();
        }

        return BuildElementTree(xamlDocument, selectedElementId, search, sourceBuildUri, out selectionExists);
    }

    private static SourceGenHotDesignElementNode? BuildElementNode(
        XElement element,
        string elementId,
        int depth,
        string? selectedElementId,
        string? search,
        string? sourceBuildUri,
        ref bool selectionExists)
    {
        var children = new List<SourceGenHotDesignElementNode>();
        var index = 0;
        foreach (var child in element.Elements())
        {
            var childId = elementId + "/" + index;
            var childNode = BuildElementNode(
                child,
                childId,
                depth + 1,
                selectedElementId,
                search,
                sourceBuildUri,
                ref selectionExists);
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
            Children: children,
            IsExpanded: depth <= 1 ||
                        !string.IsNullOrWhiteSpace(search) ||
                        isSelected ||
                        children.Any(static child => child.IsSelected || child.IsExpanded),
            DescendantCount: CountDescendants(children),
            SourceBuildUri: sourceBuildUri,
            SourceElementId: elementId);
    }

    private static IReadOnlyList<SourceGenHotDesignPropertyEntry> BuildPropertyEntries(
        XDocument? xamlDocument,
        string? selectedElementId,
        SourceGenHotDesignPropertyFilterMode mode)
    {
        if (xamlDocument?.Root is null)
        {
            return Array.Empty<SourceGenHotDesignPropertyEntry>();
        }

        var targetElement = TryFindElementById(xamlDocument.Root, string.IsNullOrWhiteSpace(selectedElementId) ? "0" : selectedElementId!);
        targetElement ??= xamlDocument.Root;
        var resolvedType = ResolveElementType(targetElement);
        var descriptors = resolvedType is null
            ? new Dictionary<string, AvaloniaPropertyDescriptor>(StringComparer.OrdinalIgnoreCase)
            : EnumerateAvaloniaProperties(resolvedType)
                .ToDictionary(static descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase);

        var properties = new Dictionary<string, SourceGenHotDesignPropertyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in targetElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var name = FormatAttributeName(targetElement, attribute.Name);
            var value = attribute.Value;
            var isMarkup = MarkupExpressionEnvelopeSemantics.IsMarkupExpression(value);
            var descriptor = TryResolvePropertyDescriptor(descriptors, name);
            var propertyType = descriptor?.PropertyType;
            var isAttached = name.Contains('.', StringComparison.Ordinal) || (descriptor?.IsAttached ?? false);
            var isReadOnly = descriptor?.IsReadOnly ?? false;
            var enumOptions = GetEnumOptions(propertyType);

            properties[name] = new SourceGenHotDesignPropertyEntry(
                Name: name,
                Value: value,
                TypeName: propertyType?.Name ?? "string",
                IsSet: true,
                IsAttached: isAttached,
                IsMarkupExtension: isMarkup,
                QuickSets: GetQuickSets(name, propertyType),
                Category: ClassifyPropertyCategory(name, propertyType, isAttached),
                Source: "Local",
                OwnerTypeName: descriptor?.OwnerType.Name ?? (resolvedType?.Name ?? string.Empty),
                EditorKind: GetEditorKind(propertyType, isMarkup),
                IsReadOnly: isReadOnly,
                CanReset: !isReadOnly,
                EnumOptions: enumOptions);
        }

        if (mode == SourceGenHotDesignPropertyFilterMode.All)
        {
            foreach (var descriptor in descriptors.Values)
            {
                if (HasPropertyEntryForDescriptor(properties, descriptor))
                {
                    continue;
                }

                var displayName = descriptor.Name;
                properties[displayName] = new SourceGenHotDesignPropertyEntry(
                    Name: displayName,
                    Value: null,
                    TypeName: descriptor.PropertyType.Name,
                    IsSet: false,
                    IsAttached: descriptor.IsAttached,
                    IsMarkupExtension: false,
                    QuickSets: GetQuickSets(displayName, descriptor.PropertyType),
                    Category: ClassifyPropertyCategory(displayName, descriptor.PropertyType, descriptor.IsAttached),
                    Source: "Default",
                    OwnerTypeName: descriptor.OwnerType.Name,
                    EditorKind: GetEditorKind(descriptor.PropertyType, isMarkupExtension: false),
                    IsReadOnly: descriptor.IsReadOnly,
                    CanReset: false,
                    EnumOptions: GetEnumOptions(descriptor.PropertyType));
            }
        }
        else
        {
            foreach (var descriptor in descriptors.Values)
            {
                var isSet = HasPropertyEntryForDescriptor(properties, descriptor);
                if (!ShouldIncludePropertyInSmartFilter(descriptor, isSet) ||
                    isSet)
                {
                    continue;
                }

                var displayName = descriptor.Name;
                properties[displayName] = new SourceGenHotDesignPropertyEntry(
                    Name: displayName,
                    Value: null,
                    TypeName: descriptor.PropertyType.Name,
                    IsSet: false,
                    IsAttached: descriptor.IsAttached,
                    IsMarkupExtension: false,
                    QuickSets: GetQuickSets(displayName, descriptor.PropertyType),
                    Category: ClassifyPropertyCategory(displayName, descriptor.PropertyType, descriptor.IsAttached),
                    Source: "Default",
                    OwnerTypeName: descriptor.OwnerType.Name,
                    EditorKind: GetEditorKind(descriptor.PropertyType, isMarkupExtension: false),
                    IsReadOnly: descriptor.IsReadOnly,
                    CanReset: false,
                    EnumOptions: GetEnumOptions(descriptor.PropertyType));
            }
        }

        return properties.Values
            .OrderByDescending(static property => property.IsPinned)
            .ThenByDescending(static property => property.IsSet)
            .ThenBy(static property => property.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SourceGenHotDesignPropertyEntry> BuildPropertyEntries(
        string? xamlText,
        string? selectedElementId,
        SourceGenHotDesignPropertyFilterMode mode)
    {
        if (!TryParseXamlDocument(xamlText, out var xamlDocument))
        {
            return Array.Empty<SourceGenHotDesignPropertyEntry>();
        }

        return BuildPropertyEntries(xamlDocument, selectedElementId, mode);
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

    private static bool HasPropertyEntryForDescriptor(
        IReadOnlyDictionary<string, SourceGenHotDesignPropertyEntry> properties,
        AvaloniaPropertyDescriptor descriptor)
    {
        if (properties.ContainsKey(descriptor.Name))
        {
            return true;
        }

        if (!descriptor.IsAttached)
        {
            return false;
        }

        var ownerQualifiedName = descriptor.OwnerType.Name + "." + descriptor.Name;
        return properties.ContainsKey(ownerQualifiedName);
    }

    private static AvaloniaPropertyDescriptor? TryResolvePropertyDescriptor(
        IReadOnlyDictionary<string, AvaloniaPropertyDescriptor> descriptors,
        string propertyName)
    {
        if (descriptors.TryGetValue(propertyName, out var descriptor))
        {
            return descriptor;
        }

        var delimiterIndex = propertyName.LastIndexOf('.');
        if (delimiterIndex <= 0 || delimiterIndex >= propertyName.Length - 1)
        {
            return null;
        }

        var localName = propertyName[(delimiterIndex + 1)..];
        return descriptors.TryGetValue(localName, out descriptor)
            ? descriptor
            : null;
    }

    private static string ClassifyPropertyCategory(string propertyName, Type? propertyType, bool isAttached)
    {
        if (isAttached)
        {
            return "Attached";
        }

        if (LayoutPropertyNames.Contains(propertyName))
        {
            return "Layout";
        }

        if (DataPropertyNames.Contains(propertyName))
        {
            return "Data";
        }

        if (InteractionPropertyNames.Contains(propertyName))
        {
            return "Interaction";
        }

        if (AppearancePropertyNames.Contains(propertyName) ||
            propertyType == typeof(IBrush) ||
            propertyType == typeof(Color))
        {
            return "Appearance";
        }

        if (propertyName.Contains("Template", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Style", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Theme", StringComparison.OrdinalIgnoreCase))
        {
            return "Styling";
        }

        if (propertyName.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Content", StringComparison.OrdinalIgnoreCase))
        {
            return "Content";
        }

        return "General";
    }

    private static string GetEditorKind(Type? propertyType, bool isMarkupExtension)
    {
        if (isMarkupExtension)
        {
            return "Markup";
        }

        if (propertyType is null || propertyType == typeof(string))
        {
            return "Text";
        }

        if (propertyType == typeof(bool) || propertyType == typeof(bool?))
        {
            return "Boolean";
        }

        if (propertyType.IsEnum)
        {
            return "Enum";
        }

        if (propertyType == typeof(double) ||
            propertyType == typeof(float) ||
            propertyType == typeof(decimal) ||
            propertyType == typeof(int) ||
            propertyType == typeof(long) ||
            propertyType == typeof(short) ||
            propertyType == typeof(uint) ||
            propertyType == typeof(ulong) ||
            propertyType == typeof(byte) ||
            propertyType == typeof(sbyte))
        {
            return "Numeric";
        }

        if (propertyType == typeof(IBrush) || propertyType == typeof(Color))
        {
            return "Brush";
        }

        return "Text";
    }

    private static bool ShouldIncludePropertyInSmartFilter(
        AvaloniaPropertyDescriptor descriptor,
        bool isSet)
    {
        if (isSet)
        {
            return true;
        }

        if (descriptor.IsAttached)
        {
            return false;
        }

        return LayoutPropertyNames.Contains(descriptor.Name) ||
               AppearancePropertyNames.Contains(descriptor.Name) ||
               DataPropertyNames.Contains(descriptor.Name) ||
               InteractionPropertyNames.Contains(descriptor.Name) ||
               string.Equals(descriptor.Name, "Name", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(descriptor.Name, "Content", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(descriptor.Name, "Text", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(descriptor.Name, "Classes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludeRuntimePropertyInSmartFilter(
        AvaloniaPropertyDescriptor descriptor,
        bool isSet)
    {
        return ShouldIncludePropertyInSmartFilter(descriptor, isSet);
    }

    private static string? FormatRuntimePropertyValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            IBrush brush => brush.ToString(),
            Thickness thickness => thickness.ToString(),
            CornerRadius cornerRadius => cornerRadius.ToString(),
            Color color => color.ToString(),
            _ => value.ToString()
        };
    }

    private static string GetRuntimePropertySource(global::Avalonia.Data.BindingPriority priority, bool isLocallySet)
    {
        if (isLocallySet || priority == global::Avalonia.Data.BindingPriority.LocalValue)
        {
            return "Local";
        }

        return priority == global::Avalonia.Data.BindingPriority.Unset
            ? "Default"
            : priority.ToString();
    }

    private static IReadOnlyList<SourceGenHotDesignToolboxCategory> BuildToolboxCategories(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents,
        string? search)
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
        foreach (var document in documents)
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
                IsProjectControl: true,
                Tags: BuildToolboxTags("Project", projectType.Name, "project", projectType.Assembly.GetName().Name)));
        }

        for (var index = 0; index < allItems.Count; index++)
        {
            var item = allItems[index];
            if (item.Tags is { Count: > 0 })
            {
                continue;
            }

            var sourceTag = item.IsProjectControl ? "project" : "framework";
            allItems[index] = item with
            {
                Tags = BuildToolboxTags(item.Category, item.Name, sourceTag, assemblyName: null)
            };
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

    private static SourceGenHotDesignDocumentDescriptor? FindDocumentByBuildUri(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents,
        string? buildUri)
    {
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            return null;
        }

        var normalizedBuildUri = buildUri.Trim();
        return documents.FirstOrDefault(document =>
            string.Equals(document.BuildUri, normalizedBuildUri, StringComparison.OrdinalIgnoreCase));
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
            var byBuildUri = FindDocumentByBuildUri(documents, buildUri);
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

    private static List<SourceGenHotDesignDocumentDescriptor> OrderDocumentsForLiveSelection(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents,
        string? preferredBuildUri)
    {
        var ordered = documents
            .OrderBy(GetDefaultDocumentScore)
            .ThenBy(static document => document.BuildUri, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(preferredBuildUri))
        {
            return ordered;
        }

        var normalizedPreferredBuildUri = preferredBuildUri.Trim();
        for (var index = 0; index < ordered.Count; index++)
        {
            if (!string.Equals(ordered[index].BuildUri, normalizedPreferredBuildUri, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var preferred = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(0, preferred);
            break;
        }

        return ordered;
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

    private static bool TryParseXamlDocument(string? xamlText, out XDocument? xamlDocument)
    {
        if (string.IsNullOrWhiteSpace(xamlText) ||
            !TryParseXaml(xamlText, out xamlDocument, out _) ||
            xamlDocument?.Root is null)
        {
            xamlDocument = null;
            return false;
        }

        return true;
    }

    private static bool TryReadCurrentXamlDocument(
        SourceGenHotDesignDocumentDescriptor? document,
        int maxHistoryEntries,
        out XDocument? xamlDocument,
        out string? xamlText)
    {
        xamlDocument = null;
        xamlText = null;
        if (document is null)
        {
            return false;
        }

        xamlText = ReadCurrentXamlText(document, maxHistoryEntries);
        return TryParseXamlDocument(xamlText, out xamlDocument);
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

    private static bool IsAmbiguousTypeMatch(IReadOnlyList<ResolvedElementCandidate> typeCandidates)
    {
        if (typeCandidates.Count <= 1)
        {
            return false;
        }

        var firstBuildUri = typeCandidates[0].BuildUri;
        for (var index = 1; index < typeCandidates.Count; index++)
        {
            if (!string.Equals(typeCandidates[index].BuildUri, firstBuildUri, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
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
        var registry = AvaloniaPropertyRegistry.Instance;

        foreach (var property in registry.GetRegistered(type))
        {
            AddAvaloniaPropertyDescriptor(descriptors, property);
        }

        foreach (var directProperty in registry.GetRegisteredDirect(type))
        {
            AddAvaloniaPropertyDescriptor(descriptors, directProperty);
        }

        foreach (var inheritedProperty in registry.GetRegisteredInherited(type))
        {
            AddAvaloniaPropertyDescriptor(descriptors, inheritedProperty);
        }

        foreach (var attachedProperty in registry.GetRegisteredAttached(type))
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
        var propertyName = property.IsAttached
            ? property.OwnerType.Name + "." + property.Name
            : property.Name;
        if (string.IsNullOrWhiteSpace(propertyName) ||
            descriptors.ContainsKey(propertyName))
        {
            return;
        }

        descriptors[propertyName] = new AvaloniaPropertyDescriptor(
            propertyName,
            property,
            property.PropertyType,
            property.IsAttached,
            property.OwnerType,
            property.IsReadOnly);
    }

    private static IReadOnlyList<string>? GetEnumOptions(Type? propertyType)
    {
        if (propertyType is null)
        {
            return null;
        }

        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (!effectiveType.IsEnum)
        {
            return null;
        }

        var names = Enum.GetNames(effectiveType);
        return names.Length == 0 ? null : names;
    }

    private static IReadOnlyList<string> BuildToolboxTags(
        string category,
        string controlName,
        string sourceTag,
        string? assemblyName)
    {
        var tags = new List<string>(4)
        {
            category,
            controlName,
            sourceTag
        };

        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            tags.Add(assemblyName!);
        }

        return tags;
    }

    private static IReadOnlyList<SourceGenHotDesignPropertyQuickSet> GetQuickSets(string propertyName, Type? propertyType = null)
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

        if (propertyType == typeof(bool) || propertyType == typeof(bool?))
        {
            return
            [
                new("True", "True"),
                new("False", "False")
            ];
        }

        if (propertyType is not null && propertyType.IsEnum)
        {
            var values = Enum.GetNames(propertyType);
            if (values.Length == 0)
            {
                return Array.Empty<SourceGenHotDesignPropertyQuickSet>();
            }

            var quickSets = new List<SourceGenHotDesignPropertyQuickSet>(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                quickSets.Add(new SourceGenHotDesignPropertyQuickSet(values[index], values[index]));
            }

            return quickSets;
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

    private readonly record struct ResolvedElementCandidate(string BuildUri, string ElementId);

    private sealed record AvaloniaPropertyDescriptor(
        string Name,
        AvaloniaProperty Property,
        Type PropertyType,
        bool IsAttached,
        Type OwnerType,
        bool IsReadOnly);
}
