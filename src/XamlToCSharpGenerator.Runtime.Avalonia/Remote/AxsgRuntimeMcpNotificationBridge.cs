using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class AxsgRuntimeMcpNotificationBridge : IDisposable
{
    private readonly McpServerCore _server;
    private readonly AxsgRuntimeMcpEventStore? _eventStore;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private readonly HashSet<string> _pendingResourceUris = new(StringComparer.Ordinal);
    private Task _notificationQueue = Task.CompletedTask;
    private bool _disposed;
    private bool _flushScheduled;

    public AxsgRuntimeMcpNotificationBridge(McpServerCore server, AxsgRuntimeMcpEventStore? eventStore = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _eventStore = eventStore;
        _shutdownToken = _shutdown.Token;
        XamlSourceGenHotReloadManager.HotReloadStatusChanged += OnHotReloadStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignStatusChanged += OnHotDesignStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignDocumentsChanged += OnHotDesignDocumentsChanged;
        XamlSourceGenStudioManager.StudioStatusChanged += OnStudioStatusChanged;
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
    }

    private void OnHotDesignDocumentsChanged(IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.HotDesignDocumentsResourceUri);
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.StudioStatusResourceUri);
    }

    private void OnStudioStatusChanged(SourceGenStudioStatusSnapshot snapshot)
    {
        EnqueueResourceUpdate(AxsgRuntimeMcpCatalog.StudioStatusResourceUri);
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

    private async Task FlushAsync()
    {
        while (true)
        {
            string[] resourceUris;
            lock (_gate)
            {
                if (_disposed)
                {
                    _flushScheduled = false;
                    return;
                }

                if (_pendingResourceUris.Count == 0)
                {
                    _flushScheduled = false;
                    return;
                }

                resourceUris = new string[_pendingResourceUris.Count];
                _pendingResourceUris.CopyTo(resourceUris);
                _pendingResourceUris.Clear();
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
