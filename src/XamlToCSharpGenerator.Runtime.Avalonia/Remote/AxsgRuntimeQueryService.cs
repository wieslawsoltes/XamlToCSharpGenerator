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
    /// Gets the current hot design workspace snapshot.
    /// </summary>
    public SourceGenHotDesignWorkspaceSnapshot GetHotDesignWorkspace(string? buildUri = null, string? search = null)
    {
        return XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search);
    }

    /// <summary>
    /// Gets the current studio session status snapshot.
    /// </summary>
    public SourceGenStudioStatusSnapshot GetStudioStatus()
    {
        return XamlSourceGenStudioManager.GetStatusSnapshot();
    }
}
