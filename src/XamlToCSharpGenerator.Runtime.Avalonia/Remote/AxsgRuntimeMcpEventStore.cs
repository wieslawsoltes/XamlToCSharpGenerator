using System;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class AxsgRuntimeMcpEventStore : IDisposable
{
    private const int MaxEventsPerStream = 128;

    private readonly object _gate = new();
    private readonly List<AxsgRuntimeMcpEventEntry> _hotReloadEvents = new();
    private readonly List<AxsgRuntimeMcpEventEntry> _hotDesignEvents = new();
    private readonly List<AxsgRuntimeMcpEventEntry> _studioEvents = new();
    private long _nextSequence;
    private bool _disposed;

    public AxsgRuntimeMcpEventStore()
    {
        XamlSourceGenHotReloadManager.HotReloadStatusChanged += OnHotReloadStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignStatusChanged += OnHotDesignStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignDocumentsChanged += OnHotDesignDocumentsChanged;
        XamlSourceGenHotDesignManager.HotDesignUpdateApplied += OnHotDesignUpdateApplied;
        XamlSourceGenHotDesignManager.HotDesignUpdateFailed += OnHotDesignUpdateFailed;
        XamlSourceGenStudioManager.StudioStatusChanged += OnStudioStatusChanged;
        XamlSourceGenStudioManager.StudioOperationStarted += OnStudioOperationStarted;
        XamlSourceGenStudioManager.StudioOperationCompleted += OnStudioOperationCompleted;

        IXamlSourceGenHotReloadEventBus eventBus = XamlSourceGenHotReloadEventBus.Instance;
        eventBus.HotReloadPipelineStarted += OnHotReloadPipelineStarted;
        eventBus.HotReloadPipelineCompleted += OnHotReloadPipelineCompleted;
        eventBus.HotReloaded += OnHotReloaded;
        eventBus.HotReloadFailed += OnHotReloadFailed;
        eventBus.HotReloadRudeEditDetected += OnHotReloadRudeEditDetected;
        eventBus.HotReloadHandlerFailed += OnHotReloadHandlerFailed;
        eventBus.HotReloadTransportStatusChanged += OnHotReloadTransportStatusChanged;
        eventBus.HotReloadRemoteOperationStatusChanged += OnHotReloadRemoteOperationStatusChanged;

        SeedInitialSnapshots();
    }

    public event Action<string>? ResourceUpdated;

    public IReadOnlyList<AxsgRuntimeMcpEventEntry> GetHotReloadEvents()
    {
        lock (_gate)
        {
            return _hotReloadEvents.ToArray();
        }
    }

    public IReadOnlyList<AxsgRuntimeMcpEventEntry> GetHotDesignEvents()
    {
        lock (_gate)
        {
            return _hotDesignEvents.ToArray();
        }
    }

    public IReadOnlyList<AxsgRuntimeMcpEventEntry> GetStudioEvents()
    {
        lock (_gate)
        {
            return _studioEvents.ToArray();
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
        XamlSourceGenHotDesignManager.HotDesignUpdateApplied -= OnHotDesignUpdateApplied;
        XamlSourceGenHotDesignManager.HotDesignUpdateFailed -= OnHotDesignUpdateFailed;
        XamlSourceGenStudioManager.StudioStatusChanged -= OnStudioStatusChanged;
        XamlSourceGenStudioManager.StudioOperationStarted -= OnStudioOperationStarted;
        XamlSourceGenStudioManager.StudioOperationCompleted -= OnStudioOperationCompleted;

        IXamlSourceGenHotReloadEventBus eventBus = XamlSourceGenHotReloadEventBus.Instance;
        eventBus.HotReloadPipelineStarted -= OnHotReloadPipelineStarted;
        eventBus.HotReloadPipelineCompleted -= OnHotReloadPipelineCompleted;
        eventBus.HotReloaded -= OnHotReloaded;
        eventBus.HotReloadFailed -= OnHotReloadFailed;
        eventBus.HotReloadRudeEditDetected -= OnHotReloadRudeEditDetected;
        eventBus.HotReloadHandlerFailed -= OnHotReloadHandlerFailed;
        eventBus.HotReloadTransportStatusChanged -= OnHotReloadTransportStatusChanged;
        eventBus.HotReloadRemoteOperationStatusChanged -= OnHotReloadRemoteOperationStatusChanged;
    }

    private void OnHotReloadStatusChanged(SourceGenHotReloadStatus status)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "statusChanged",
            "Hot reload status changed.",
            new
            {
                status.IsEnabled,
                status.IsIdePollingFallbackEnabled,
                status.RegisteredTypeCount,
                status.RegisteredBuildUriCount,
                transportMode = status.TransportMode.ToString()
            });
    }

    private void OnHotReloadPipelineStarted(SourceGenHotReloadUpdateContext context)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "pipelineStarted",
            "Hot reload pipeline started.",
            BuildHotReloadContextPayload(context));
    }

    private void OnHotReloadPipelineCompleted(SourceGenHotReloadUpdateContext context)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "pipelineCompleted",
            "Hot reload pipeline completed.",
            BuildHotReloadContextPayload(context));
    }

    private void OnHotReloaded(Type[]? types)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "reloaded",
            "Hot reload applied updated types.",
            new
            {
                typeNames = types?.Select(static type => type.FullName).Where(static name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>()
            });
    }

    private void OnHotReloadFailed(Type type, Exception exception)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "failed",
            "Hot reload failed for a type.",
            new
            {
                typeName = type.FullName,
                exceptionType = exception.GetType().FullName,
                exception.Message
            });
    }

    private void OnHotReloadRudeEditDetected(Type type, Exception exception)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "rudeEditDetected",
            "Hot reload detected a rude edit.",
            new
            {
                typeName = type.FullName,
                exceptionType = exception.GetType().FullName,
                exception.Message
            });
    }

    private void OnHotReloadHandlerFailed(Type type, string phase, Exception exception)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "handlerFailed",
            "Hot reload handler failed.",
            new
            {
                typeName = type.FullName,
                phase,
                exceptionType = exception.GetType().FullName,
                exception.Message
            });
    }

    private void OnHotReloadTransportStatusChanged(SourceGenHotReloadTransportStatus status)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "transportStatusChanged",
            status.Message,
            new
            {
                kind = status.Kind.ToString(),
                status.TransportName,
                mode = status.Mode.ToString(),
                status.TimestampUtc,
                status.IsFallback,
                exceptionType = status.Exception?.GetType().FullName,
                exceptionMessage = status.Exception?.Message
            });
    }

    private void OnHotReloadRemoteOperationStatusChanged(SourceGenHotReloadRemoteOperationStatus status)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotReloadEventsResourceUri,
            _hotReloadEvents,
            "remoteOperationStatusChanged",
            "Hot reload remote operation status changed.",
            new
            {
                status.OperationId,
                status.RequestId,
                status.CorrelationId,
                state = status.State.ToString(),
                status.StartedAtUtc,
                status.CompletedAtUtc,
                request = new
                {
                    status.Request.ApplyAll,
                    status.Request.TypeNames,
                    status.Request.BuildUris,
                    status.Request.Trigger
                },
                result = status.Result is null
                    ? null
                    : new
                    {
                        status.Result.IsSuccess,
                        status.Result.Message,
                        state = status.Result.State.ToString(),
                        status.Result.Diagnostics
                    },
                status.Diagnostics
            });
    }

    private void OnHotDesignStatusChanged(SourceGenHotDesignStatus status)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotDesignEventsResourceUri,
            _hotDesignEvents,
            "statusChanged",
            "Hot design status changed.",
            AxsgRuntimePayloadBuilder.BuildHotDesignStatusPayload(status));
    }

    private void OnHotDesignDocumentsChanged(IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotDesignEventsResourceUri,
            _hotDesignEvents,
            "documentsChanged",
            "Hot design document set changed.",
            AxsgRuntimePayloadBuilder.BuildHotDesignDocumentsPayload(documents));
    }

    private void OnHotDesignUpdateApplied(SourceGenHotDesignApplyResult result)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotDesignEventsResourceUri,
            _hotDesignEvents,
            "updateApplied",
            result.Message,
            new
            {
                result.Succeeded,
                result.BuildUri,
                targetTypeName = result.TargetType?.FullName,
                result.SourcePath,
                result.SourcePersisted,
                result.MinimalDiffApplied,
                result.MinimalDiffStart,
                result.MinimalDiffRemovedLength,
                result.MinimalDiffInsertedLength,
                result.HotReloadObserved,
                result.RuntimeFallbackApplied
            });
    }

    private void OnHotDesignUpdateFailed(SourceGenHotDesignUpdateRequest request, Exception exception)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.HotDesignEventsResourceUri,
            _hotDesignEvents,
            "updateFailed",
            "Hot design update failed.",
            new
            {
                request.BuildUri,
                request.TargetTypeName,
                xamlTextLength = request.XamlText?.Length ?? 0,
                request.PersistChangesToSource,
                request.WaitForHotReload,
                request.FallbackToRuntimeApplyOnTimeout,
                exceptionType = exception.GetType().FullName,
                exception.Message
            });
    }

    private void OnStudioStatusChanged(SourceGenStudioStatusSnapshot snapshot)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.StudioEventsResourceUri,
            _studioEvents,
            "statusChanged",
            "Studio status changed.",
            new
            {
                snapshot.IsEnabled,
                snapshot.SessionId,
                currentState = snapshot.CurrentState.ToString(),
                snapshot.RegisteredDocumentCount,
                snapshot.ActiveScopeCount,
                remote = new
                {
                    snapshot.Remote.IsEnabled,
                    snapshot.Remote.IsListening,
                    snapshot.Remote.Host,
                    snapshot.Remote.Port,
                    snapshot.Remote.ActiveClientCount,
                    snapshot.Remote.LastError,
                    snapshot.Remote.VncEndpoint,
                    snapshot.Remote.UpdatedAtUtc
                }
            });
    }

    private void OnStudioOperationStarted(SourceGenStudioOperationStatus status)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.StudioEventsResourceUri,
            _studioEvents,
            "operationStarted",
            "Studio operation started.",
            BuildStudioOperationPayload(status));
    }

    private void OnStudioOperationCompleted(SourceGenStudioOperationStatus status)
    {
        AppendEvent(
            AxsgRuntimeMcpCatalog.StudioEventsResourceUri,
            _studioEvents,
            "operationCompleted",
            "Studio operation completed.",
            BuildStudioOperationPayload(status));
    }

    private void AppendEvent(
        string resourceUri,
        List<AxsgRuntimeMcpEventEntry> target,
        string kind,
        string message,
        object? data)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _nextSequence++;
            target.Add(new AxsgRuntimeMcpEventEntry(
                _nextSequence,
                kind,
                DateTimeOffset.UtcNow,
                message,
                data));

            while (target.Count > MaxEventsPerStream)
            {
                target.RemoveAt(0);
            }
        }

        TryPublishResourceUpdated(resourceUri);
    }

    private void SeedInitialSnapshots()
    {
        OnHotReloadStatusChanged(XamlSourceGenHotReloadManager.GetStatus());
        OnHotDesignStatusChanged(XamlSourceGenHotDesignManager.GetStatus());

        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        if (documents.Count > 0)
        {
            OnHotDesignDocumentsChanged(documents);
        }

        OnStudioStatusChanged(XamlSourceGenStudioManager.GetStatusSnapshot());
    }

    private void TryPublishResourceUpdated(string resourceUri)
    {
        Action<string>? handlers = ResourceUpdated;
        if (handlers is null)
        {
            return;
        }

        Delegate[] invocationList = handlers.GetInvocationList();
        for (var index = 0; index < invocationList.Length; index++)
        {
            try
            {
                ((Action<string>)invocationList[index]).Invoke(resourceUri);
            }
            catch
            {
                // Runtime event notifications are best effort and must not break hot reload or studio flows.
            }
        }
    }

    private static object BuildHotReloadContextPayload(SourceGenHotReloadUpdateContext context)
    {
        return new
        {
            trigger = context.Trigger.ToString(),
            requestedTypeNames = context.RequestedTypes?.Select(static type => type.FullName).Where(static name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>(),
            reloadedTypeNames = context.ReloadedTypes.Select(static type => type.FullName).Where(static name => !string.IsNullOrWhiteSpace(name)).ToArray(),
            context.OperationCount,
            context.OperationId,
            context.RequestId,
            context.CorrelationId
        };
    }

    private static object BuildStudioOperationPayload(SourceGenStudioOperationStatus status)
    {
        return new
        {
            status.OperationId,
            status.RequestId,
            status.CorrelationId,
            state = status.State.ToString(),
            status.StartedAtUtc,
            status.CompletedAtUtc,
            request = new
            {
                status.Request.BuildUri,
                status.Request.TargetTypeName,
                scopeKind = status.Request.ScopeKind.ToString(),
                status.Request.ScopeId
            },
            result = status.Result is null ? null : AxsgRuntimePayloadBuilder.BuildStudioUpdateResultPayload(status.Result),
            status.Diagnostics
        };
    }
}

internal sealed record AxsgRuntimeMcpEventEntry(
    long Sequence,
    string Kind,
    DateTimeOffset TimestampUtc,
    string Message,
    object? Data);
