using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides transport-neutral hot reload runtime control shared by MCP and other remote adapters.
/// </summary>
public sealed class AxsgRuntimeHotReloadService
{
    private readonly AxsgRuntimeQueryService _runtimeQueryService;

    /// <summary>
    /// Creates a new hot reload runtime service.
    /// </summary>
    public AxsgRuntimeHotReloadService()
        : this(new AxsgRuntimeQueryService())
    {
    }

    internal AxsgRuntimeHotReloadService(AxsgRuntimeQueryService runtimeQueryService)
    {
        _runtimeQueryService = runtimeQueryService ?? throw new System.ArgumentNullException(nameof(runtimeQueryService));
    }

    /// <summary>
    /// Enables the source-generated hot reload runtime.
    /// </summary>
    public SourceGenHotReloadStatus Enable()
    {
        XamlSourceGenHotReloadManager.Enable();
        return _runtimeQueryService.GetHotReloadStatus();
    }

    /// <summary>
    /// Disables the source-generated hot reload runtime.
    /// </summary>
    public SourceGenHotReloadStatus Disable()
    {
        XamlSourceGenHotReloadManager.Disable();
        return _runtimeQueryService.GetHotReloadStatus();
    }

    /// <summary>
    /// Toggles the source-generated hot reload runtime.
    /// </summary>
    public SourceGenHotReloadStatus Toggle()
    {
        if (_runtimeQueryService.GetHotReloadStatus().IsEnabled)
        {
            return Disable();
        }

        return Enable();
    }

    /// <summary>
    /// Gets the current hot reload runtime status snapshot.
    /// </summary>
    public SourceGenHotReloadStatus GetStatus()
    {
        return _runtimeQueryService.GetHotReloadStatus();
    }

    /// <summary>
    /// Gets the currently tracked hot reload documents.
    /// </summary>
    public IReadOnlyList<SourceGenHotReloadTrackedDocumentDescriptor> GetTrackedDocuments()
    {
        return _runtimeQueryService.GetHotReloadTrackedDocuments();
    }

    /// <summary>
    /// Gets the current hot reload transport status.
    /// </summary>
    public SourceGenHotReloadTransportStatus? GetTransportStatus()
    {
        return _runtimeQueryService.GetHotReloadTransportStatus();
    }

    /// <summary>
    /// Gets the last remote hot reload operation status.
    /// </summary>
    public SourceGenHotReloadRemoteOperationStatus? GetLastOperation()
    {
        return _runtimeQueryService.GetHotReloadLastOperation();
    }
}
