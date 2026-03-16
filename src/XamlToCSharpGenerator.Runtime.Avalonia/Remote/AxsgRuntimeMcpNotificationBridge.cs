using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class AxsgRuntimeMcpNotificationBridge : IDisposable
{
    private readonly McpServerCore _server;
    private readonly AxsgRuntimeQueryService _runtimeQueryService;
    private readonly AxsgRuntimeMcpEventStore? _eventStore;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private readonly HashSet<string> _pendingResourceUris = new(StringComparer.Ordinal);
    private Task _notificationQueue = Task.CompletedTask;
    private bool _disposed;
    private bool _flushScheduled;
    private bool _resourcesListChangedPending;

    public AxsgRuntimeMcpNotificationBridge(
        McpServerCore server,
        AxsgRuntimeQueryService runtimeQueryService,
        AxsgRuntimeMcpEventStore? eventStore = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _runtimeQueryService = runtimeQueryService ?? throw new ArgumentNullException(nameof(runtimeQueryService));
        _eventStore = eventStore;
        _shutdownToken = _shutdown.Token;
        XamlSourceGenHotReloadManager.HotReloadStatusChanged += OnHotReloadStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignStatusChanged += OnHotDesignStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignDocumentsChanged += OnHotDesignDocumentsChanged;
        XamlSourceGenStudioManager.StudioStatusChanged += OnStudioStatusChanged;
        XamlSourceGenHotDesignTool.WorkspaceChanged += OnHotDesignWorkspaceChanged;
        if (_eventStore is not null)
        {
            _eventStore.ResourceUpdated += OnEventStoreResourceUpdated;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        XamlSourceGenHotReloadManager.HotReloadStatusChanged -= OnHotReloadStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignStatusChanged -= OnHotDesignStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignDocumentsChanged -= OnHotDesignDocumentsChanged;
        XamlSourceGenStudioManager.StudioStatusChanged -= OnStudioStatusChanged;
        XamlSourceGenHotDesignTool.WorkspaceChanged -= OnHotDesignWorkspaceChanged;
        if (_eventStore is not null)
        {
            _eventStore.ResourceUpdated -= OnEventStoreResourceUpdated;
        }
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private void OnHotReloadStatusChanged(SourceGenHotReloadStatus status)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotReloadStatusResourceUri);
    }

    private void OnHotDesignStatusChanged(SourceGenHotDesignStatus status)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotDesignStatusResourceUri);
        EnqueueHotDesignWorkspaceResourceUpdates();
    }

    private void OnHotDesignDocumentsChanged(IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotDesignDocumentsResourceUri);
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.StudioStatusResourceUri);
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.StudioScopesResourceUri);
        EnqueueHotDesignWorkspaceResourceUpdates(documents);
        EnqueueResourcesListChanged();
    }

    private void OnStudioStatusChanged(SourceGenStudioStatusSnapshot snapshot)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.StudioStatusResourceUri);
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.StudioScopesResourceUri);
        EnqueueHotDesignWorkspaceResourceUpdates();
    }

    private void OnHotDesignWorkspaceChanged()
    {
        EnqueueHotDesignWorkspaceResourceUpdates();
    }

    private void OnEventStoreResourceUpdated(string resourceUri)
    {
        EnqueueResourceUpdate(resourceUri);
    }

    private void EnqueueResourceUpdate(string resourceUri)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingResourceUris.Add(resourceUri);
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
            _notificationQueue = _notificationQueue.ContinueWith(
                static (antecedent, state) => ((AxsgRuntimeMcpNotificationBridge)state!).FlushAsync(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private void EnqueueResourcesListChanged()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _resourcesListChangedPending = true;
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
            _notificationQueue = _notificationQueue.ContinueWith(
                static (antecedent, state) => ((AxsgRuntimeMcpNotificationBridge)state!).FlushAsync(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private void EnqueueHotDesignWorkspaceResourceUpdates(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor>? documents = null)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotDesignCurrentWorkspaceResourceUri);
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotDesignSelectedDocumentResourceUri);
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotDesignSelectedElementResourceUri);

        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> sourceDocuments = documents ?? _runtimeQueryService.GetHotDesignDocuments();
        foreach (string resourceUri in AxsgRuntimeMcpCatalog.EnumerateHotDesignWorkspaceResourceUris(sourceDocuments))
        {
            if (!string.Equals(resourceUri, AxsgRuntimeMcpCatalog.HotDesignCurrentWorkspaceResourceUri, StringComparison.Ordinal))
            {
                EnqueueResourceUpdate(resourceUri);
            }
        }
    }

    private async Task FlushAsync()
    {
        while (true)
        {
            string[] resourceUris;
            bool resourcesListChanged;
            lock (_gate)
            {
                if (_disposed)
                {
                    _flushScheduled = false;
                    return;
                }

                if (_pendingResourceUris.Count == 0 && !_resourcesListChangedPending)
                {
                    _flushScheduled = false;
                    return;
                }

                resourcesListChanged = _resourcesListChangedPending;
                _resourcesListChangedPending = false;
                resourceUris = new string[_pendingResourceUris.Count];
                _pendingResourceUris.CopyTo(resourceUris);
                _pendingResourceUris.Clear();
            }

            if (resourcesListChanged)
            {
                try
                {
                    await _server.NotifyResourcesListChangedAsync(_shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Notification delivery is best effort.
                }
            }

            for (var index = 0; index < resourceUris.Length; index++)
            {
                try
                {
                    await _server.NotifyResourceUpdatedAsync(resourceUris[index], _shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Notification delivery is best effort.
                }
            }
        }
    }
}
