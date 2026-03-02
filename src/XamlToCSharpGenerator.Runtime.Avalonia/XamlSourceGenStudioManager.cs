using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenStudioManager
{
    private const string TraceEnvVarName = "AXSG_STUDIO_TRACE";

    private static readonly object Sync = new();
    private static readonly List<SourceGenStudioOperationStatus> Operations = new();

    private static SourceGenStudioOptions ActiveOptions = new();
    private static SourceGenStudioOperationState CurrentState = SourceGenStudioOperationState.Ready;
    private static SourceGenStudioRemoteStatus RemoteStatus = SourceGenStudioRemoteStatus.Disabled();
    private static Guid SessionId = Guid.Empty;
    private static long OperationSequence;
    private static bool? TraceEnabledCached;

    static XamlSourceGenStudioManager()
    {
        XamlSourceGenHotReloadManager.HotReloadPipelineStarted += OnHotReloadPipelineStarted;
        XamlSourceGenHotReloadManager.HotReloadPipelineCompleted += OnHotReloadPipelineCompleted;
        XamlSourceGenHotReloadManager.HotReloadFailed += OnHotReloadFailed;
    }

    public static bool IsEnabled { get; private set; }

    public static event Action<bool>? StudioModeChanged;

    public static event Action<SourceGenStudioStatusSnapshot>? StudioStatusChanged;

    public static event Action<SourceGenStudioOperationStatus>? StudioOperationStarted;

    public static event Action<SourceGenStudioOperationStatus>? StudioOperationCompleted;

    public static void Enable(SourceGenStudioOptions? options = null)
    {
        SourceGenStudioStatusSnapshot? snapshot;
        lock (Sync)
        {
            if (options is not null)
            {
                ActiveOptions = options.Clone();
            }

            IsEnabled = true;
            if (SessionId == Guid.Empty)
            {
                SessionId = Guid.NewGuid();
            }

            ConfigureHotDesignManagerLocked();
            RemoteStatus = new SourceGenStudioRemoteStatus(
                IsEnabled: ActiveOptions.EnableRemoteDesign,
                IsListening: false,
                Host: ActiveOptions.RemoteHost,
                Port: ActiveOptions.RemotePort,
                ActiveClientCount: 0,
                LastError: null,
                VncEndpoint: ActiveOptions.VncEndpoint,
                UpdatedAtUtc: DateTimeOffset.UtcNow);
            snapshot = CreateSnapshotLocked();
        }

        StudioModeChanged?.Invoke(true);
        PublishStatusChanged(snapshot);
    }

    public static void Disable()
    {
        SourceGenStudioStatusSnapshot? snapshot;
        lock (Sync)
        {
            IsEnabled = false;
            CurrentState = SourceGenStudioOperationState.Ready;
            RemoteStatus = SourceGenStudioRemoteStatus.Disabled(ActiveOptions.VncEndpoint);
            XamlSourceGenHotDesignManager.Disable();
            snapshot = CreateSnapshotLocked();
        }

        StudioModeChanged?.Invoke(false);
        PublishStatusChanged(snapshot);
    }

    public static void Configure(Action<SourceGenStudioOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        SourceGenStudioStatusSnapshot? snapshot;
        lock (Sync)
        {
            var clone = ActiveOptions.Clone();
            configure(clone);
            ActiveOptions = clone;

            if (IsEnabled)
            {
                ConfigureHotDesignManagerLocked();
            }

            if (!IsEnabled || !RemoteStatus.IsListening)
            {
                RemoteStatus = new SourceGenStudioRemoteStatus(
                    IsEnabled: clone.EnableRemoteDesign,
                    IsListening: false,
                    Host: clone.RemoteHost,
                    Port: clone.RemotePort,
                    ActiveClientCount: 0,
                    LastError: null,
                    VncEndpoint: clone.VncEndpoint,
                    UpdatedAtUtc: DateTimeOffset.UtcNow);
            }

            snapshot = CreateSnapshotLocked();
        }

        PublishStatusChanged(snapshot);
    }

    public static Guid StartSession()
    {
        SourceGenStudioStatusSnapshot? snapshot;
        Guid sessionId;
        lock (Sync)
        {
            SessionId = Guid.NewGuid();
            CurrentState = SourceGenStudioOperationState.Ready;
            snapshot = CreateSnapshotLocked();
            sessionId = SessionId;
        }

        PublishStatusChanged(snapshot);
        return sessionId;
    }

    public static void StopSession()
    {
        SourceGenStudioStatusSnapshot? snapshot;
        lock (Sync)
        {
            SessionId = Guid.Empty;
            CurrentState = SourceGenStudioOperationState.Ready;
            Operations.Clear();
            snapshot = CreateSnapshotLocked();
        }

        PublishStatusChanged(snapshot);
    }

    public static SourceGenStudioStatusSnapshot GetStatusSnapshot()
    {
        lock (Sync)
        {
            return CreateSnapshotLocked();
        }
    }

    internal static void UpdateRemoteStatus(SourceGenStudioRemoteStatus status)
    {
        if (status is null)
        {
            return;
        }

        SourceGenStudioStatusSnapshot? snapshot;
        lock (Sync)
        {
            RemoteStatus = status;
            snapshot = CreateSnapshotLocked();
        }

        PublishStatusChanged(snapshot);
    }

    public static IReadOnlyList<SourceGenStudioScopeDescriptor> GetScopes()
    {
        lock (Sync)
        {
            return BuildScopesLocked(XamlSourceGenHotDesignManager.GetRegisteredDocuments());
        }
    }

    public static async ValueTask<SourceGenStudioUpdateResult> ApplyUpdateAsync(
        SourceGenStudioUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SourceGenStudioOperationStatus startedOperation;
        SourceGenStudioWaitMode waitMode;
        SourceGenStudioFallbackPolicy fallbackPolicy;
        TimeSpan timeout;
        bool persistChangesToSource;
        bool enabled;

        lock (Sync)
        {
            enabled = IsEnabled;
            if (SessionId == Guid.Empty)
            {
                SessionId = Guid.NewGuid();
            }

            waitMode = request.WaitMode ?? ActiveOptions.WaitMode;
            fallbackPolicy = request.FallbackPolicy ?? ActiveOptions.FallbackPolicy;
            timeout = request.Timeout ?? ActiveOptions.UpdateTimeout;
            persistChangesToSource = request.PersistChangesToSource ?? ActiveOptions.PersistChangesToSource;

            var operationId = Interlocked.Increment(ref OperationSequence);
            startedOperation = new SourceGenStudioOperationStatus(
                OperationId: operationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Applying,
                StartedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null,
                Request: request,
                Result: null,
                Diagnostics: null);

            Operations.Add(startedOperation);
            TrimOperationHistoryLocked();
            CurrentState = SourceGenStudioOperationState.Applying;
        }

        StudioOperationStarted?.Invoke(startedOperation);
        PublishStatusChanged(GetStatusSnapshot());

        if (!enabled)
        {
            var disabledResult = new SourceGenStudioUpdateResult(
                Succeeded: false,
                Message: "Studio mode is disabled.",
                OperationId: startedOperation.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Failed,
                SourcePersisted: false,
                LocalUpdateObserved: false,
                RuntimeFallbackApplied: false,
                BuildUri: request.BuildUri,
                TargetType: request.TargetType);
            CompleteOperation(startedOperation, disabledResult);
            return disabledResult;
        }

        var hotDesignRequest = new SourceGenHotDesignUpdateRequest
        {
            BuildUri = request.BuildUri,
            TargetType = request.TargetType,
            TargetTypeName = request.TargetTypeName,
            XamlText = request.XamlText,
            PersistChangesToSource = persistChangesToSource,
            WaitForHotReload = waitMode != SourceGenStudioWaitMode.None,
            FallbackToRuntimeApplyOnTimeout =
                fallbackPolicy is SourceGenStudioFallbackPolicy.RuntimeApplyOnTimeout ||
                fallbackPolicy is SourceGenStudioFallbackPolicy.RuntimeApplyOnNoServerUpdate
        };

        SourceGenHotDesignApplyResult applyResult;
        try
        {
            using var timeoutCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero)
            {
                timeoutCancellationSource.CancelAfter(timeout);
            }

            applyResult = await XamlSourceGenHotDesignManager
                .ApplyUpdateAsync(hotDesignRequest, timeoutCancellationSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            var canceledResult = new SourceGenStudioUpdateResult(
                Succeeded: false,
                Message: "Studio update canceled.",
                OperationId: startedOperation.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Canceled,
                SourcePersisted: false,
                LocalUpdateObserved: false,
                RuntimeFallbackApplied: false,
                BuildUri: request.BuildUri,
                TargetType: request.TargetType,
                Error: ex);
            CompleteOperation(startedOperation, canceledResult);
            return canceledResult;
        }
        catch (OperationCanceledException ex)
        {
            var timedOutResult = new SourceGenStudioUpdateResult(
                Succeeded: false,
                Message: "Studio update timed out.",
                OperationId: startedOperation.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.TimedOut,
                SourcePersisted: false,
                LocalUpdateObserved: false,
                RuntimeFallbackApplied: false,
                BuildUri: request.BuildUri,
                TargetType: request.TargetType,
                Error: ex);
            CompleteOperation(startedOperation, timedOutResult);
            return timedOutResult;
        }

        var state = ResolveOperationState(applyResult);
        var studioResult = new SourceGenStudioUpdateResult(
            Succeeded: applyResult.Succeeded,
            Message: applyResult.Message,
            OperationId: startedOperation.OperationId,
            RequestId: request.RequestId,
            CorrelationId: request.CorrelationId,
            State: state,
            SourcePersisted: applyResult.SourcePersisted,
            LocalUpdateObserved: applyResult.HotReloadObserved,
            RuntimeFallbackApplied: applyResult.RuntimeFallbackApplied,
            BuildUri: applyResult.BuildUri ?? request.BuildUri,
            TargetType: applyResult.TargetType ?? request.TargetType,
            Error: applyResult.Error,
            Diagnostics: BuildDiagnosticsList(applyResult));

        CompleteOperation(startedOperation, studioResult);
        return studioResult;
    }

    public static SourceGenStudioUpdateResult ApplyUpdate(
        SourceGenStudioUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ApplyUpdateAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    private static void CompleteOperation(
        SourceGenStudioOperationStatus startedOperation,
        SourceGenStudioUpdateResult result)
    {
        SourceGenStudioOperationStatus completedOperation;
        SourceGenStudioStatusSnapshot? snapshot;
        lock (Sync)
        {
            var diagnostics = result.Diagnostics ?? Array.Empty<string>();
            completedOperation = startedOperation with
            {
                State = result.State,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Result = result,
                Diagnostics = diagnostics
            };

            var replaced = false;
            for (var index = 0; index < Operations.Count; index++)
            {
                if (Operations[index].OperationId != startedOperation.OperationId)
                {
                    continue;
                }

                Operations[index] = completedOperation;
                replaced = true;
                break;
            }

            if (!replaced)
            {
                Operations.Add(completedOperation);
            }

            TrimOperationHistoryLocked();
            CurrentState = SourceGenStudioOperationState.Ready;
            snapshot = CreateSnapshotLocked();
        }

        StudioOperationCompleted?.Invoke(completedOperation);
        PublishStatusChanged(snapshot);
    }

    private static SourceGenStudioStatusSnapshot CreateSnapshotLocked()
    {
        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        var scopes = BuildScopesLocked(documents);
        var session = SessionId == Guid.Empty ? Guid.Empty : SessionId;
        return new SourceGenStudioStatusSnapshot(
            IsEnabled,
            session,
            CurrentState,
            documents.Count,
            scopes.Count,
            scopes,
            Operations.ToArray(),
            ActiveOptions.Clone(),
            RemoteStatus);
    }

    private static IReadOnlyList<SourceGenStudioScopeDescriptor> BuildScopesLocked(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<SourceGenStudioScopeDescriptor>();
        }

        var scopes = new List<SourceGenStudioScopeDescriptor>(documents.Count);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            var scopeKind = ResolveScopeKind(document);
            var scopeId = document.BuildUri;
            var displayName = !string.IsNullOrWhiteSpace(document.RootType.FullName)
                ? document.RootType.FullName!
                : document.BuildUri;

            scopes.Add(new SourceGenStudioScopeDescriptor(
                scopeKind,
                scopeId,
                displayName,
                document.RootType,
                document.BuildUri));
        }

        return scopes;
    }

    private static SourceGenStudioScopeKind ResolveScopeKind(SourceGenHotDesignDocumentDescriptor descriptor)
    {
        if (typeof(global::Avalonia.Application).IsAssignableFrom(descriptor.RootType))
        {
            return SourceGenStudioScopeKind.Application;
        }

        if (typeof(global::Avalonia.Controls.TopLevel).IsAssignableFrom(descriptor.RootType))
        {
            return SourceGenStudioScopeKind.TopLevelWindow;
        }

        if (descriptor.ArtifactKind is SourceGenHotDesignArtifactKind.Template)
        {
            return SourceGenStudioScopeKind.Template;
        }

        return SourceGenStudioScopeKind.RootControl;
    }

    private static void ConfigureHotDesignManagerLocked()
    {
        var hotDesignOptions = new SourceGenHotDesignOptions
        {
            PersistChangesToSource = ActiveOptions.PersistChangesToSource,
            WaitForHotReload = ActiveOptions.WaitMode != SourceGenStudioWaitMode.None,
            HotReloadWaitTimeout = ActiveOptions.UpdateTimeout,
            FallbackToRuntimeApplyOnTimeout =
                ActiveOptions.FallbackPolicy == SourceGenStudioFallbackPolicy.RuntimeApplyOnTimeout ||
                ActiveOptions.FallbackPolicy == SourceGenStudioFallbackPolicy.RuntimeApplyOnNoServerUpdate,
            EnableTracing = ActiveOptions.EnableTracing
        };

        XamlSourceGenHotDesignManager.Enable(hotDesignOptions);
    }

    private static void TrimOperationHistoryLocked()
    {
        var maxEntries = Math.Max(10, ActiveOptions.MaxOperationHistoryEntries);
        while (Operations.Count > maxEntries)
        {
            Operations.RemoveAt(0);
        }
    }

    private static SourceGenStudioOperationState ResolveOperationState(SourceGenHotDesignApplyResult result)
    {
        if (result.Succeeded)
        {
            return SourceGenStudioOperationState.Succeeded;
        }

        if (result.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return SourceGenStudioOperationState.TimedOut;
        }

        return SourceGenStudioOperationState.Failed;
    }

    private static IReadOnlyList<string> BuildDiagnosticsList(SourceGenHotDesignApplyResult result)
    {
        if (result.Error is null)
        {
            return Array.Empty<string>();
        }

        return new[] { result.Error.Message };
    }

    private static void OnHotReloadPipelineStarted(SourceGenHotReloadUpdateContext context)
    {
        SourceGenStudioStatusSnapshot? snapshot = null;
        lock (Sync)
        {
            if (!IsEnabled)
            {
                return;
            }

            CurrentState = SourceGenStudioOperationState.Applying;
            snapshot = CreateSnapshotLocked();
        }

        PublishStatusChanged(snapshot);
    }

    private static void OnHotReloadPipelineCompleted(SourceGenHotReloadUpdateContext context)
    {
        SourceGenStudioStatusSnapshot? snapshot = null;
        lock (Sync)
        {
            if (!IsEnabled)
            {
                return;
            }

            CurrentState = SourceGenStudioOperationState.Ready;
            snapshot = CreateSnapshotLocked();
        }

        PublishStatusChanged(snapshot);
    }

    private static void OnHotReloadFailed(Type type, Exception exception)
    {
        SourceGenStudioStatusSnapshot? snapshot = null;
        lock (Sync)
        {
            if (!IsEnabled)
            {
                return;
            }

            CurrentState = SourceGenStudioOperationState.Failed;
            snapshot = CreateSnapshotLocked();
        }

        PublishStatusChanged(snapshot);
    }

    private static void PublishStatusChanged(SourceGenStudioStatusSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        StudioStatusChanged?.Invoke(snapshot);
        Trace("Studio status changed. State=" + snapshot.CurrentState + ", Session=" + snapshot.SessionId + ".");
    }

    private static bool IsTraceEnabled()
    {
        if (TraceEnabledCached.HasValue)
        {
            return TraceEnabledCached.Value;
        }

        var value = Environment.GetEnvironmentVariable(TraceEnvVarName);
        var enabled = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        TraceEnabledCached = enabled;
        return enabled;
    }

    private static void Trace(string message)
    {
        if (!IsTraceEnabled())
        {
            return;
        }

        try
        {
            Debug.WriteLine("[AXSG.Studio] " + message);
            Console.WriteLine("[AXSG.Studio] " + message);
        }
        catch
        {
            // Best-effort tracing only.
        }
    }
}
