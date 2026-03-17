using System;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides transport-neutral runtime queries shared by MCP, hot-design studio, and preview tooling.
/// </summary>
public sealed class AxsgRuntimeQueryService
{
    /// <summary>
    /// Gets the current hot reload runtime status snapshot.
    /// </summary>
    public SourceGenHotReloadStatus GetHotReloadStatus()
    {
        return XamlSourceGenHotReloadTool.GetStatus();
    }

    /// <summary>
    /// Lists the currently tracked hot reload documents.
    /// </summary>
    public IReadOnlyList<SourceGenHotReloadTrackedDocumentDescriptor> GetHotReloadTrackedDocuments()
    {
        return XamlSourceGenHotReloadManager.GetTrackedDocuments();
    }

    /// <summary>
    /// Gets the current hot reload transport status.
    /// </summary>
    public SourceGenHotReloadTransportStatus? GetHotReloadTransportStatus()
    {
        return XamlSourceGenHotReloadManager.GetStatus().LastTransportStatus;
    }

    /// <summary>
    /// Gets the last remote hot reload operation status.
    /// </summary>
    public SourceGenHotReloadRemoteOperationStatus? GetHotReloadLastOperation()
    {
        return XamlSourceGenHotReloadManager.GetStatus().LastRemoteOperationStatus;
    }

    /// <summary>
    /// Gets the current hot design runtime status snapshot.
    /// </summary>
    public SourceGenHotDesignStatus GetHotDesignStatus()
    {
        return XamlSourceGenHotDesignTool.GetStatus();
    }

    /// <summary>
    /// Lists the currently registered hot design documents.
    /// </summary>
    public IReadOnlyList<SourceGenHotDesignDocumentDescriptor> GetHotDesignDocuments()
    {
        return XamlSourceGenHotDesignTool.ListDocuments();
    }

    /// <summary>
    /// Gets the currently selected hot design document.
    /// </summary>
    public SourceGenHotDesignDocumentDescriptor? GetHotDesignSelectedDocument(string? buildUri = null)
    {
        SourceGenHotDesignWorkspaceSnapshot workspace = GetHotDesignWorkspace(buildUri, search: null);
        string? activeBuildUri = workspace.ActiveBuildUri;
        if (string.IsNullOrWhiteSpace(activeBuildUri))
        {
            return null;
        }

        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents = workspace.Documents;
        for (int index = 0; index < documents.Count; index++)
        {
            SourceGenHotDesignDocumentDescriptor document = documents[index];
            if (string.Equals(document.BuildUri, activeBuildUri, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the currently selected hot design element.
    /// </summary>
    public SourceGenHotDesignElementNode? GetHotDesignSelectedElement(string? buildUri = null)
    {
        SourceGenHotDesignWorkspaceSnapshot workspace = GetHotDesignWorkspace(buildUri, search: null);
        string? selectedElementId = workspace.SelectedElementId;
        if (string.IsNullOrWhiteSpace(selectedElementId))
        {
            return null;
        }

        return FindElement(workspace.Elements, selectedElementId);
    }

    /// <summary>
    /// Gets the current hot design workspace snapshot.
    /// </summary>
    public SourceGenHotDesignWorkspaceSnapshot GetHotDesignWorkspace(string? buildUri = null, string? search = null)
    {
        return XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search);
    }

    /// <summary>
    /// Gets the current hot design hit-test mode.
    /// </summary>
    public SourceGenHotDesignHitTestMode GetHotDesignHitTestMode()
    {
        return XamlSourceGenHotDesignTool.GetHitTestMode();
    }

    /// <summary>
    /// Gets the current studio session status snapshot.
    /// </summary>
    public SourceGenStudioStatusSnapshot GetStudioStatus()
    {
        return XamlSourceGenStudioManager.GetStatusSnapshot();
    }

    /// <summary>
    /// Gets the current studio scopes.
    /// </summary>
    public IReadOnlyList<SourceGenStudioScopeDescriptor> GetStudioScopes()
    {
        return XamlSourceGenStudioManager.GetScopes();
    }

    private static SourceGenHotDesignElementNode? FindElement(
        IReadOnlyList<SourceGenHotDesignElementNode> elements,
        string selectedElementId)
    {
        for (int index = 0; index < elements.Count; index++)
        {
            SourceGenHotDesignElementNode element = elements[index];
            if (string.Equals(element.Id, selectedElementId, StringComparison.Ordinal))
            {
                return element;
            }

            SourceGenHotDesignElementNode? child = FindElement(element.Children, selectedElementId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }
}
