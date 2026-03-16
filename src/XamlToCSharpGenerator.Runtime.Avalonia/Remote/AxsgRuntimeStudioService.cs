using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides transport-neutral studio runtime control and update operations shared by MCP and remote adapters.
/// </summary>
public sealed class AxsgRuntimeStudioService
{
    private readonly AxsgRuntimeQueryService _runtimeQueryService;

    /// <summary>
    /// Creates a new studio runtime service.
    /// </summary>
    public AxsgRuntimeStudioService()
        : this(new AxsgRuntimeQueryService())
    {
    }

    internal AxsgRuntimeStudioService(AxsgRuntimeQueryService runtimeQueryService)
    {
        _runtimeQueryService = runtimeQueryService ?? throw new ArgumentNullException(nameof(runtimeQueryService));
    }

    /// <summary>
    /// Enables studio mode and returns the updated studio status snapshot.
    /// </summary>
    public SourceGenStudioStatusSnapshot Enable(SourceGenStudioOptions? options = null)
    {
        XamlSourceGenStudioManager.Enable(options);
        return _runtimeQueryService.GetStudioStatus();
    }

    /// <summary>
    /// Disables studio mode and returns the updated studio status snapshot.
    /// </summary>
    public SourceGenStudioStatusSnapshot Disable()
    {
        XamlSourceGenStudioManager.Disable();
        return _runtimeQueryService.GetStudioStatus();
    }

    /// <summary>
    /// Applies a full studio options snapshot and returns the updated studio status.
    /// </summary>
    public SourceGenStudioStatusSnapshot Configure(SourceGenStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        XamlSourceGenStudioManager.Configure(target =>
        {
            target.PersistChangesToSource = options.PersistChangesToSource;
            target.WaitMode = options.WaitMode;
            target.UpdateTimeout = options.UpdateTimeout;
            target.FallbackPolicy = options.FallbackPolicy;
            target.ShowOverlayIndicator = options.ShowOverlayIndicator;
            target.EnableExternalWindow = options.EnableExternalWindow;
            target.AutoOpenStudioWindowOnStartup = options.AutoOpenStudioWindowOnStartup;
            target.EnableTracing = options.EnableTracing;
            target.CanvasLayoutMode = options.CanvasLayoutMode;
            target.MaxOperationHistoryEntries = options.MaxOperationHistoryEntries;
            target.EnableRemoteDesign = options.EnableRemoteDesign;
            target.RemoteHost = options.RemoteHost;
            target.RemotePort = options.RemotePort;
            target.VncEndpoint = options.VncEndpoint;
            target.AutoOpenVncViewerOnDesktop = options.AutoOpenVncViewerOnDesktop;
        });

        return _runtimeQueryService.GetStudioStatus();
    }

    /// <summary>
    /// Starts a new studio session.
    /// </summary>
    public Guid StartSession()
    {
        return XamlSourceGenStudioManager.StartSession();
    }

    /// <summary>
    /// Stops the current studio session and returns the updated studio status.
    /// </summary>
    public SourceGenStudioStatusSnapshot StopSession()
    {
        XamlSourceGenStudioManager.StopSession();
        return _runtimeQueryService.GetStudioStatus();
    }

    /// <summary>
    /// Gets the current studio scopes.
    /// </summary>
    public IReadOnlyList<SourceGenStudioScopeDescriptor> GetScopes()
    {
        return XamlSourceGenStudioManager.GetScopes();
    }

    /// <summary>
    /// Applies a studio update request.
    /// </summary>
    public ValueTask<SourceGenStudioUpdateResult> ApplyUpdateAsync(
        SourceGenStudioUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return XamlSourceGenStudioManager.ApplyUpdateAsync(NormalizeUpdateRequest(request), cancellationToken);
    }

    /// <summary>
    /// Applies XAML text to the active or specified studio document.
    /// </summary>
    public ValueTask<SourceGenStudioUpdateResult> ApplyDocumentTextAsync(
        string? buildUri,
        string? xamlText,
        string? requestId = null,
        long? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (xamlText is null)
        {
            throw new InvalidOperationException("xamlText is required.");
        }

        string resolvedBuildUri = ResolveRequestedOrActiveBuildUri(buildUri, "buildUri is required.");
        SourceGenHotDesignDocumentDescriptor? document = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .FirstOrDefault(candidate => string.Equals(candidate.BuildUri, resolvedBuildUri, StringComparison.OrdinalIgnoreCase));

        var request = new SourceGenStudioUpdateRequest
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            BuildUri = resolvedBuildUri,
            TargetType = document?.RootType,
            TargetTypeName = document?.RootType.FullName,
            XamlText = xamlText
        };

        return XamlSourceGenStudioManager.ApplyUpdateAsync(request, cancellationToken);
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

    private SourceGenStudioUpdateRequest NormalizeUpdateRequest(SourceGenStudioUpdateRequest request)
    {
        string resolvedBuildUri = ResolveRequestedOrActiveBuildUri(request.BuildUri, "buildUri is required.");
        SourceGenHotDesignDocumentDescriptor? document = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .FirstOrDefault(candidate => string.Equals(candidate.BuildUri, resolvedBuildUri, StringComparison.OrdinalIgnoreCase));

        return new SourceGenStudioUpdateRequest
        {
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            BuildUri = resolvedBuildUri,
            TargetType = request.TargetType ?? document?.RootType,
            TargetTypeName = request.TargetTypeName ?? request.TargetType?.FullName ?? document?.RootType.FullName,
            ScopeKind = request.ScopeKind,
            ScopeId = request.ScopeId,
            XamlText = request.XamlText,
            WaitMode = request.WaitMode,
            FallbackPolicy = request.FallbackPolicy,
            PersistChangesToSource = request.PersistChangesToSource,
            Timeout = request.Timeout
        };
    }
}
