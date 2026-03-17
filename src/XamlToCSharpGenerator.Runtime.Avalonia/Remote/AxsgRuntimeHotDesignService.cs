using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides transport-neutral hot design runtime mutations shared by MCP, studio remote design, and future adapters.
/// </summary>
public sealed class AxsgRuntimeHotDesignService
{
    private readonly AxsgRuntimeQueryService _runtimeQueryService;

    /// <summary>
    /// Creates a new hot design runtime service.
    /// </summary>
    public AxsgRuntimeHotDesignService()
        : this(new AxsgRuntimeQueryService())
    {
    }

    internal AxsgRuntimeHotDesignService(AxsgRuntimeQueryService runtimeQueryService)
    {
        _runtimeQueryService = runtimeQueryService ?? throw new ArgumentNullException(nameof(runtimeQueryService));
    }

    /// <summary>
    /// Enables hot design and returns the updated runtime status.
    /// </summary>
    public SourceGenHotDesignStatus Enable(SourceGenHotDesignOptions? options = null)
    {
        XamlSourceGenHotDesignTool.Enable(options);
        return _runtimeQueryService.GetHotDesignStatus();
    }

    /// <summary>
    /// Disables hot design and returns the updated runtime status.
    /// </summary>
    public SourceGenHotDesignStatus Disable()
    {
        XamlSourceGenHotDesignTool.Disable();
        return _runtimeQueryService.GetHotDesignStatus();
    }

    /// <summary>
    /// Toggles hot design and returns the updated runtime status.
    /// </summary>
    public SourceGenHotDesignStatus Toggle()
    {
        XamlSourceGenHotDesignTool.Toggle();
        return _runtimeQueryService.GetHotDesignStatus();
    }

    /// <summary>
    /// Sets the active hot-design document and returns the updated workspace snapshot.
    /// </summary>
    public SourceGenHotDesignWorkspaceSnapshot SelectDocument(string? buildUri)
    {
        string resolvedBuildUri = RequireRegisteredBuildUri(buildUri, "buildUri is required.");
        XamlSourceGenHotDesignTool.SelectDocument(resolvedBuildUri);
        return _runtimeQueryService.GetHotDesignWorkspace(resolvedBuildUri, search: null);
    }

    /// <summary>
    /// Sets the active hot-design element selection and returns the updated workspace snapshot.
    /// </summary>
    public SourceGenHotDesignWorkspaceSnapshot SelectElement(string? buildUri, string? elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw new InvalidOperationException("elementId is required.");
        }

        string resolvedBuildUri = ResolveRequestedOrActiveBuildUri(
            buildUri,
            "No active document is available for element selection.");
        RequireRegisteredBuildUri(resolvedBuildUri);

        SourceGenHotDesignWorkspaceSnapshot workspaceBeforeSelection = _runtimeQueryService.GetHotDesignWorkspace(resolvedBuildUri, search: null);
        if (!ContainsElementId(workspaceBeforeSelection.Elements, elementId))
        {
            throw new InvalidOperationException(
                "No element with id '" + elementId + "' exists in buildUri '" + resolvedBuildUri + "'.");
        }

        XamlSourceGenHotDesignTool.SelectElement(resolvedBuildUri, elementId);
        return _runtimeQueryService.GetHotDesignWorkspace(resolvedBuildUri, search: null);
    }

    /// <summary>
    /// Applies updated XAML text to a hot-design document.
    /// </summary>
    public async ValueTask<SourceGenHotDesignApplyResult> ApplyDocumentTextAsync(
        string? buildUri,
        string? xamlText,
        CancellationToken cancellationToken = default)
    {
        if (xamlText is null)
        {
            throw new InvalidOperationException("xamlText is required.");
        }

        string resolvedBuildUri = ResolveRequestedOrActiveBuildUri(buildUri, "buildUri is required.");
        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool
            .ApplyDocumentTextAsync(resolvedBuildUri, xamlText, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Applies a hot-design property update request.
    /// </summary>
    public async ValueTask<SourceGenHotDesignApplyResult> ApplyPropertyUpdateAsync(
        SourceGenHotDesignPropertyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool
            .ApplyPropertyUpdateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Inserts a hot-design element.
    /// </summary>
    public async ValueTask<SourceGenHotDesignApplyResult> InsertElementAsync(
        SourceGenHotDesignElementInsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool
            .InsertElementAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Removes a hot-design element.
    /// </summary>
    public async ValueTask<SourceGenHotDesignApplyResult> RemoveElementAsync(
        SourceGenHotDesignElementRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool
            .RemoveElementAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Undoes the last hot-design change for the target document.
    /// </summary>
    public async ValueTask<SourceGenHotDesignApplyResult> UndoAsync(
        string? buildUri = null,
        CancellationToken cancellationToken = default)
    {
        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool
            .UndoAsync(buildUri, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Redoes the last hot-design change for the target document.
    /// </summary>
    public async ValueTask<SourceGenHotDesignApplyResult> RedoAsync(
        string? buildUri = null,
        CancellationToken cancellationToken = default)
    {
        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool
            .RedoAsync(buildUri, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Sets the hot-design workspace mode.
    /// </summary>
    public void SetWorkspaceMode(SourceGenHotDesignWorkspaceMode mode)
    {
        XamlSourceGenHotDesignTool.SetWorkspaceMode(mode);
    }

    /// <summary>
    /// Sets the property filter mode for the hot-design workspace.
    /// </summary>
    public void SetPropertyFilterMode(SourceGenHotDesignPropertyFilterMode mode)
    {
        XamlSourceGenHotDesignTool.SetPropertyFilterMode(mode);
    }

    /// <summary>
    /// Sets the hit-test mode for the hot-design workspace.
    /// </summary>
    public void SetHitTestMode(SourceGenHotDesignHitTestMode mode)
    {
        XamlSourceGenHotDesignTool.SetHitTestMode(mode);
    }

    /// <summary>
    /// Toggles the visibility of a hot-design panel.
    /// </summary>
    public bool TogglePanel(SourceGenHotDesignPanelKind panel)
    {
        return XamlSourceGenHotDesignTool.TogglePanel(panel);
    }

    /// <summary>
    /// Sets the visibility of a hot-design panel.
    /// </summary>
    public void SetPanelVisibility(SourceGenHotDesignPanelKind panel, bool visible)
    {
        XamlSourceGenHotDesignTool.SetPanelVisibility(panel, visible);
    }

    /// <summary>
    /// Sets the canvas zoom level.
    /// </summary>
    public void SetCanvasZoom(double zoom)
    {
        XamlSourceGenHotDesignTool.SetCanvasZoom(zoom);
    }

    /// <summary>
    /// Sets the canvas form factor.
    /// </summary>
    public void SetCanvasFormFactor(string formFactor, double? width = null, double? height = null)
    {
        XamlSourceGenHotDesignTool.SetCanvasFormFactor(formFactor, width, height);
    }

    /// <summary>
    /// Sets the canvas theme.
    /// </summary>
    public void SetCanvasTheme(bool darkTheme)
    {
        XamlSourceGenHotDesignTool.SetCanvasTheme(darkTheme);
    }

    private string ResolveRequestedOrActiveBuildUri(string? buildUri, string missingMessage)
    {
        if (!string.IsNullOrWhiteSpace(buildUri))
        {
            return buildUri.Trim();
        }

        string? activeBuildUri = _runtimeQueryService.GetHotDesignWorkspace().ActiveBuildUri;
        if (string.IsNullOrWhiteSpace(activeBuildUri))
        {
            throw new InvalidOperationException(missingMessage);
        }

        return activeBuildUri;
    }

    private static string RequireRegisteredBuildUri(string? buildUri, string? missingMessage = null)
    {
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            throw new InvalidOperationException(missingMessage ?? "buildUri is required.");
        }

        string resolvedBuildUri = buildUri.Trim();
        bool documentExists = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .Any(candidate => string.Equals(candidate.BuildUri, resolvedBuildUri, StringComparison.OrdinalIgnoreCase));
        if (!documentExists)
        {
            throw new InvalidOperationException(
                "No registered document matches buildUri '" + resolvedBuildUri + "'.");
        }

        return resolvedBuildUri;
    }

    private static bool ContainsElementId(IReadOnlyList<SourceGenHotDesignElementNode> elements, string elementId)
    {
        if (elements.Count == 0)
        {
            return false;
        }

        for (int index = 0; index < elements.Count; index++)
        {
            SourceGenHotDesignElementNode element = elements[index];
            if (string.Equals(element.Id, elementId, StringComparison.Ordinal))
            {
                return true;
            }

            if (ContainsElementId(element.Children, elementId))
            {
                return true;
            }
        }

        return false;
    }
}
