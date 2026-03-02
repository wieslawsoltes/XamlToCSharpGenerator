using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: MetadataUpdateHandler(typeof(XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotReloadManager
{
    private const int SourcePathReloadRetryCount = 5;
    private const string TraceEnvVarName = "AXSG_HOTRELOAD_TRACE";
    private const string TransportModeEnvVarName = "AXSG_HOTRELOAD_TRANSPORT_MODE";
    private const string HandshakeTimeoutEnvVarName = "AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS";
    private const string IosHotReloadEnabledEnvVarName = "AXSG_IOS_HOTRELOAD_ENABLED";
    private const int DefaultHandshakeTimeoutMs = 3000;
    private const int MaxProcessedRemoteOperationHistory = 256;
    private static readonly TimeSpan DuplicateReloadWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MetadataVsPollingDedupWindow = TimeSpan.FromSeconds(2);

    private static readonly object Sync = new();
    private static readonly Dictionary<Type, List<WeakReference<object>>> Instances = new();
    private static readonly Dictionary<Type, ReloadRegistration> Registrations = new();
    private static readonly Dictionary<Type, SourcePathWatchState> IdeSourcePathWatchers = new();
    private static readonly Dictionary<Type, string> BuildUrisByType = new();
    private static readonly Dictionary<Type, Type> ReplacementTypeMap = new();
    private static readonly List<RegisteredHandler> Handlers = new();
    private static readonly HashSet<string> HandlerKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<Type> PendingReloadTypes = new();
    private static readonly HashSet<long> ProcessedRemoteOperationIds = new();
    private static readonly Queue<long> ProcessedRemoteOperationOrder = new();
    private static readonly IXamlSourceGenUriMapper UriMapper = XamlSourceGenUriMapper.Default;

    private static Timer? IdePollingTimer;
    private static int IdePollingIntervalMs = 1000;
    private static bool? TraceEnabledCached;
    private static bool ReloadInProgress;
    private static bool PendingReloadAllTypes;
    private static string? LastAcceptedReloadKey;
    private static SourceGenHotReloadTrigger? LastAcceptedReloadTrigger;
    private static DateTimeOffset LastAcceptedReloadTimestampUtc;
    private static bool TransportInitialized;
    private static SourceGenHotReloadTransportMode TransportMode = SourceGenHotReloadTransportMode.Auto;
    private static TimeSpan HandshakeTimeout = TimeSpan.FromMilliseconds(DefaultHandshakeTimeoutMs);
    private static ISourceGenHotReloadTransport? ActiveTransport;
    private static MetadataUpdateTransport? MetadataTransport;
    private static RemoteSocketTransport? RemoteTransport;
    private static ISourceGenHotReloadRemoteOperationTransport? ActiveRemoteOperationTransport;
    private static Timer? MetadataHandshakeTimer;
    private static bool MetadataHandshakePending;
    private static bool MetadataHandshakeCompleted;
    private static bool SuppressStatefulControlTreeStateTransfer;

    static XamlSourceGenHotReloadManager()
    {
        lock (Sync)
        {
            AddDefaultHandlersLocked();
        }
    }

    public static event Action<Type[]?>? HotReloaded;

    public static event Action<Type, Exception>? HotReloadFailed;

    public static event Action<Type, Exception>? HotReloadRudeEditDetected;

    public static event Action<Type, string, Exception>? HotReloadHandlerFailed;

    public static event Action<SourceGenHotReloadUpdateContext>? HotReloadPipelineStarted;

    public static event Action<SourceGenHotReloadUpdateContext>? HotReloadPipelineCompleted;

    public static event Action<SourceGenHotReloadTransportStatus>? HotReloadTransportStatusChanged;

    public static event Action<SourceGenHotReloadRemoteOperationStatus>? HotReloadRemoteOperationStatusChanged;

    public static bool IsEnabled { get; private set; } = true;

    public static bool IsIdePollingFallbackEnabled { get; private set; }

    public static void Enable()
    {
        IsEnabled = true;
        EnsureTransportInitialized();
    }

    public static void Disable()
    {
        IsEnabled = false;
        lock (Sync)
        {
            ResetTransportStateLocked();
        }
    }

    public static void Register(object instance, Action<object> reloadAction, string? sourcePath = null)
    {
        Register(instance, reloadAction, new SourceGenHotReloadRegistrationOptions
        {
            SourcePath = sourcePath
        });
    }

    public static void Register(
        object instance,
        Action<object> reloadAction,
        SourceGenHotReloadRegistrationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(reloadAction);

        var type = NormalizeType(instance.GetType());
        var registration = new ReloadRegistration(
            reloadAction,
            options?.BeforeReload,
            options?.CaptureState,
            options?.RestoreState,
            options?.AfterReload);

        lock (Sync)
        {
            Registrations[type] = registration;
            ReplacementTypeMap[type] = type;

            if (!Instances.TryGetValue(type, out var references))
            {
                references = new List<WeakReference<object>>();
                Instances[type] = references;
            }

            PruneDeadReferences(references);
            if (!ContainsReference(references, instance))
            {
                references.Add(new WeakReference<object>(instance));
                Trace("Registered instance for type '" + type.FullName + "'.");
            }

            var sourcePath = options?.SourcePath;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                var normalizedSourcePath = NormalizeSourcePath(sourcePath);
                if (normalizedSourcePath is not null)
                {
                    IdeSourcePathWatchers[type] = SourcePathWatchState.Create(normalizedSourcePath);
                    Trace("Registered source path watcher for type '" + type.FullName + "': " + normalizedSourcePath);
                }
            }

            if (TryNormalizeBuildUri(options?.BuildUri, out var buildUri))
            {
                BuildUrisByType[type] = buildUri;
            }
        }
    }

    public static void RegisterReplacementTypeMapping(Type replacementType, Type originalType)
    {
        ArgumentNullException.ThrowIfNull(replacementType);
        ArgumentNullException.ThrowIfNull(originalType);

        var normalizedReplacementType = NormalizeType(replacementType);
        var normalizedOriginalType = NormalizeType(originalType);
        lock (Sync)
        {
            ReplacementTypeMap[normalizedReplacementType] = normalizedOriginalType;
            ReplacementTypeMap[normalizedOriginalType] = normalizedOriginalType;
        }
    }

    public static void RegisterHandler(ISourceGenHotReloadHandler handler, Type? elementType = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (Sync)
        {
            AddHandlerLocked(handler, elementType, "manual");
        }
    }

    public static void ResetHandlersToDefaults()
    {
        lock (Sync)
        {
            Handlers.Clear();
            HandlerKeys.Clear();
            AddDefaultHandlersLocked();
        }
    }

    public static void ClearRegistrations()
    {
        lock (Sync)
        {
            Instances.Clear();
            Registrations.Clear();
            IdeSourcePathWatchers.Clear();
            BuildUrisByType.Clear();
            ReplacementTypeMap.Clear();
            PendingReloadTypes.Clear();
            PendingReloadAllTypes = false;
            LastAcceptedReloadKey = null;
            LastAcceptedReloadTrigger = null;
            LastAcceptedReloadTimestampUtc = default;
            ProcessedRemoteOperationIds.Clear();
            ProcessedRemoteOperationOrder.Clear();
            ResetTransportStateLocked();
        }
    }

    public static void EnableIdePollingFallback(int intervalMs = 1000)
    {
        if (intervalMs < 100)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs), intervalMs, "Polling interval must be at least 100ms.");
        }

        lock (Sync)
        {
            IdePollingIntervalMs = intervalMs;
            IsIdePollingFallbackEnabled = true;
            Trace("IDE polling fallback enabled (interval " + intervalMs + "ms).");
            StartIdePollingTimerLocked();
        }
    }

    public static void DisableIdePollingFallback()
    {
        lock (Sync)
        {
            DisableIdePollingFallbackLocked();
            Trace("IDE polling fallback disabled.");
        }
    }

    public static bool TryEnableIdePollingFallbackFromEnvironment(int intervalMs = 1000)
    {
        if (!ShouldEnableIdePollingFallbackFromEnvironment())
        {
            return false;
        }

        EnableIdePollingFallback(intervalMs);
        return true;
    }

    public static bool ShouldEnableIdePollingFallbackFromEnvironment()
    {
        if (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS())
        {
            return true;
        }

        var modifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        return string.Equals(modifiableAssemblies, "debug", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureTransportInitialized()
    {
        SourceGenHotReloadTransportMode configuredMode;
        SourceGenHotReloadTransportCapabilities metadataCapabilities;
        SourceGenHotReloadTransportCapabilities remoteCapabilities;

        lock (Sync)
        {
            if (TransportInitialized)
            {
                return;
            }

            TransportMode = ResolveTransportModeFromEnvironment();
            HandshakeTimeout = ResolveHandshakeTimeoutFromEnvironment();
            MetadataTransport = new MetadataUpdateTransport(Trace);
            RemoteTransport = new RemoteSocketTransport();
            TransportInitialized = true;

            configuredMode = TransportMode;
            metadataCapabilities = MetadataTransport.Capabilities;
            remoteCapabilities = RemoteTransport.Capabilities;
        }

        Trace(
            "Hot reload transport initialization. Mode: " + configuredMode +
            ", handshake timeout: " + HandshakeTimeout.TotalMilliseconds + "ms.");
        Trace(
            "Transport capability probe: MetadataUpdate(" + FormatTransportCapabilities(metadataCapabilities) +
            "), RemoteSocket(" + FormatTransportCapabilities(remoteCapabilities) + ").");

        SelectInitialTransport(configuredMode);
    }

    private static void SelectInitialTransport(SourceGenHotReloadTransportMode mode)
    {
        switch (mode)
        {
            case SourceGenHotReloadTransportMode.MetadataOnly:
                _ = TrySelectAndStartTransport(GetMetadataTransport(), mode, isFallback: false);
                break;
            case SourceGenHotReloadTransportMode.RemoteOnly:
                _ = TrySelectAndStartTransport(GetRemoteTransport(), mode, isFallback: false);
                break;
            default:
                if (!TrySelectAndStartTransport(GetMetadataTransport(), mode, isFallback: false))
                {
                    _ = TrySelectAndStartTransport(GetRemoteTransport(), mode, isFallback: true);
                }
                break;
        }
    }

    private static bool TrySelectAndStartTransport(
        ISourceGenHotReloadTransport? transport,
        SourceGenHotReloadTransportMode mode,
        bool isFallback)
    {
        if (transport is null)
        {
            return false;
        }

        var capabilities = transport.Capabilities;
        var selectedMessage = "Selected transport '" + transport.Name + "'. Capabilities: " + FormatTransportCapabilities(capabilities) + ".";
        PublishTransportStatus(SourceGenHotReloadTransportStatusKind.TransportSelected, transport.Name, mode, selectedMessage, isFallback);

        if (!capabilities.IsSupported)
        {
            PublishTransportStatus(
                SourceGenHotReloadTransportStatusKind.HandshakeFailed,
                transport.Name,
                mode,
                "Transport '" + transport.Name + "' is not supported in current runtime/environment: " + capabilities.Diagnostic + ".",
                isFallback);
            return false;
        }

        PublishTransportStatus(
            SourceGenHotReloadTransportStatusKind.HandshakeStarted,
            transport.Name,
            mode,
            "Handshake started for transport '" + transport.Name + "'.",
            isFallback);

        SourceGenHotReloadHandshakeResult handshakeResult;
        try
        {
            handshakeResult = transport.StartHandshake(HandshakeTimeout);
        }
        catch (Exception ex)
        {
            PublishTransportStatus(
                SourceGenHotReloadTransportStatusKind.HandshakeFailed,
                transport.Name,
                mode,
                "Handshake threw for transport '" + transport.Name + "': " + ex.Message,
                isFallback,
                ex);
            return false;
        }

        if (!handshakeResult.IsSuccess)
        {
            PublishTransportStatus(
                SourceGenHotReloadTransportStatusKind.HandshakeFailed,
                transport.Name,
                mode,
                handshakeResult.Message,
                isFallback,
                handshakeResult.Exception);
            return false;
        }

        var publishCompleted = false;
        lock (Sync)
        {
            if (!ReferenceEquals(ActiveTransport, transport))
            {
                try
                {
                    ActiveTransport?.Stop();
                }
                catch
                {
                    // Best effort transport stop only.
                }
            }

            ActiveTransport = transport;
            RewireRemoteTransportSubscriptionLocked(transport);

            if (transport is MetadataUpdateTransport && handshakeResult.IsPending)
            {
                MetadataHandshakePending = true;
                MetadataHandshakeCompleted = false;
                if (ShouldAttemptRemoteFallbackFromMetadataTimeoutLocked())
                {
                    ScheduleMetadataHandshakeTimeoutLocked();
                }
                else
                {
                    StopMetadataHandshakeTimerLocked();
                }
            }
            else
            {
                MetadataHandshakePending = false;
                MetadataHandshakeCompleted = true;
                StopMetadataHandshakeTimerLocked();
                publishCompleted = true;
            }
        }

        if (publishCompleted)
        {
            PublishTransportStatus(
                SourceGenHotReloadTransportStatusKind.HandshakeCompleted,
                transport.Name,
                mode,
                handshakeResult.Message,
                isFallback);
        }

        return true;
    }

    private static MetadataUpdateTransport? GetMetadataTransport()
    {
        lock (Sync)
        {
            MetadataTransport ??= new MetadataUpdateTransport(Trace);
            return MetadataTransport;
        }
    }

    private static RemoteSocketTransport? GetRemoteTransport()
    {
        lock (Sync)
        {
            RemoteTransport ??= new RemoteSocketTransport();
            return RemoteTransport;
        }
    }

    private static void RewireRemoteTransportSubscriptionLocked(ISourceGenHotReloadTransport? transport)
    {
        if (ActiveRemoteOperationTransport is not null)
        {
            ActiveRemoteOperationTransport.RemoteUpdateReceived -= OnRemoteUpdateReceived;
        }

        ActiveRemoteOperationTransport = transport as ISourceGenHotReloadRemoteOperationTransport;
        if (ActiveRemoteOperationTransport is not null)
        {
            ActiveRemoteOperationTransport.RemoteUpdateReceived += OnRemoteUpdateReceived;
        }
    }

    private static void OnRemoteUpdateReceived(SourceGenHotReloadRemoteUpdateRequest request)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var started = new SourceGenHotReloadRemoteOperationStatus(
            OperationId: request.OperationId,
            RequestId: request.RequestId,
            CorrelationId: request.CorrelationId,
            State: SourceGenStudioOperationState.Applying,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: null,
            Request: request);
        PublishRemoteOperationStatus(started);

        SourceGenHotReloadRemoteUpdateResult result;
        try
        {
            result = ProcessRemoteUpdateRequest(request);
        }
        catch (Exception ex)
        {
            result = new SourceGenHotReloadRemoteUpdateResult(
                OperationId: request.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Failed,
                IsSuccess: false,
                Message: "Unhandled remote update exception: " + ex.Message,
                Diagnostics: [ex.ToString()]);
        }

        PublishRemoteOperationStatus(
            started with
            {
                State = result.State,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Result = result,
                Diagnostics = result.Diagnostics
            });

        TryPublishRemoteOperationResult(result);
    }

    private static SourceGenHotReloadRemoteUpdateResult ProcessRemoteUpdateRequest(SourceGenHotReloadRemoteUpdateRequest request)
    {
        if (!IsEnabled)
        {
            return new SourceGenHotReloadRemoteUpdateResult(
                OperationId: request.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Failed,
                IsSuccess: false,
                Message: "Hot reload manager is disabled.",
                Diagnostics: ["Enable hot reload manager before remote apply operations."]);
        }

        if (request.OperationId <= 0)
        {
            return new SourceGenHotReloadRemoteUpdateResult(
                OperationId: request.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Failed,
                IsSuccess: false,
                Message: "Remote update request must provide a positive operationId.",
                Diagnostics: ["Invalid operationId."]);
        }

        if (IsProcessedRemoteOperation(request.OperationId))
        {
            return new SourceGenHotReloadRemoteUpdateResult(
                OperationId: request.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Succeeded,
                IsSuccess: true,
                Message: "Duplicate remote operation ignored; operation was already applied.");
        }

        var diagnostics = new List<string>();
        var resolvedTypes = ResolveRemoteRequestTypes(request, diagnostics);
        if (!request.ApplyAll && (resolvedTypes is null || resolvedTypes.Length == 0))
        {
            return new SourceGenHotReloadRemoteUpdateResult(
                OperationId: request.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Failed,
                IsSuccess: false,
                Message: "Remote update request did not resolve any tracked hot reload types.",
                Diagnostics: diagnostics);
        }

        var failures = new List<string>();
        void OnFailure(Type type, Exception exception)
        {
            var message = (type.FullName ?? type.Name) + ": " + exception.Message;
            failures.Add(message);
        }

        HotReloadFailed += OnFailure;
        try
        {
            UpdateApplicationCore(
                request.ApplyAll ? null : resolvedTypes,
                SourceGenHotReloadTrigger.RemoteTransport,
                request);
        }
        finally
        {
            HotReloadFailed -= OnFailure;
        }

        if (failures.Count > 0)
        {
            if (diagnostics.Count == 0)
            {
                diagnostics.AddRange(failures);
            }
            else
            {
                diagnostics.AddRange(failures);
            }

            return new SourceGenHotReloadRemoteUpdateResult(
                OperationId: request.OperationId,
                RequestId: request.RequestId,
                CorrelationId: request.CorrelationId,
                State: SourceGenStudioOperationState.Failed,
                IsSuccess: false,
                Message: "Remote update operation completed with failures.",
                Diagnostics: diagnostics);
        }

        MarkRemoteOperationProcessed(request.OperationId);
        return new SourceGenHotReloadRemoteUpdateResult(
            OperationId: request.OperationId,
            RequestId: request.RequestId,
            CorrelationId: request.CorrelationId,
            State: SourceGenStudioOperationState.Succeeded,
            IsSuccess: true,
            Message: request.ApplyAll
                ? "Remote update applied for all tracked types."
                : "Remote update applied for " + resolvedTypes!.Length + " resolved types.",
            Diagnostics: diagnostics.Count > 0 ? diagnostics : null);
    }

    private static Type[] ResolveRemoteRequestTypes(SourceGenHotReloadRemoteUpdateRequest request, List<string> diagnostics)
    {
        var resolved = new HashSet<Type>();
        var hasAnyInput = false;

        foreach (var typeName in request.TypeNames)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                continue;
            }

            hasAnyInput = true;
            if (TryResolveTypeByName(typeName, out var resolvedType))
            {
                resolved.Add(NormalizeUpdatedType(resolvedType));
            }
            else
            {
                diagnostics.Add("Unable to resolve type '" + typeName + "'.");
            }
        }

        var normalizedBuildUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var buildUri in request.BuildUris)
        {
            if (string.IsNullOrWhiteSpace(buildUri))
            {
                continue;
            }

            hasAnyInput = true;
            normalizedBuildUris.Add(UriMapper.Normalize(buildUri));
        }

        if (normalizedBuildUris.Count > 0)
        {
            lock (Sync)
            {
                foreach (var trackedType in Instances.Keys)
                {
                    if (!TryGetBuildUriForTypeOrDeclaringLocked(trackedType, out var buildUri))
                    {
                        continue;
                    }

                    if (normalizedBuildUris.Contains(buildUri))
                    {
                        resolved.Add(trackedType);
                    }
                }
            }
        }

        if (!request.ApplyAll && !hasAnyInput)
        {
            diagnostics.Add("Remote update request contains no typeNames/buildUris and applyAll=false.");
        }

        if (!request.ApplyAll && normalizedBuildUris.Count > 0 && resolved.Count == 0)
        {
            diagnostics.Add("No tracked registrations matched requested buildUris.");
        }

        if (resolved.Count == 0)
        {
            return Array.Empty<Type>();
        }

        var types = new Type[resolved.Count];
        resolved.CopyTo(types);
        return types;
    }

    private static bool TryResolveTypeByName(string typeName, out Type type)
    {
        var normalizedTypeName = NormalizeRemoteTypeName(typeName);
        if (normalizedTypeName.Length == 0)
        {
            type = default!;
            return false;
        }

        lock (Sync)
        {
            if (TryResolveTypeByNameFromCollectionLocked(Instances.Keys, normalizedTypeName, out type) ||
                TryResolveTypeByNameFromCollectionLocked(ReplacementTypeMap.Keys, normalizedTypeName, out type) ||
                TryResolveTypeByNameFromCollectionLocked(ReplacementTypeMap.Values, normalizedTypeName, out type))
            {
                return true;
            }
        }

        var knownTypes = SourceGenKnownTypeRegistry.GetRegisteredTypes();
        for (var index = 0; index < knownTypes.Count; index++)
        {
            if (DoesRemoteTypeNameMatch(knownTypes[index], normalizedTypeName))
            {
                type = knownTypes[index];
                return true;
            }
        }

        type = default!;
        return false;
    }

    private static bool TryResolveTypeByNameFromCollectionLocked(
        IEnumerable<Type> candidates,
        string normalizedTypeName,
        out Type resolvedType)
    {
        foreach (var candidate in candidates)
        {
            if (!DoesRemoteTypeNameMatch(candidate, normalizedTypeName))
            {
                continue;
            }

            resolvedType = candidate;
            return true;
        }

        resolvedType = default!;
        return false;
    }

    private static bool DoesRemoteTypeNameMatch(Type candidate, string normalizedTypeName)
    {
        var candidateFullName = candidate.FullName;
        if (!string.IsNullOrWhiteSpace(candidateFullName))
        {
            if (string.Equals(candidateFullName, normalizedTypeName, StringComparison.Ordinal) ||
                string.Equals(StripGenericArity(candidateFullName), normalizedTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var candidateName = candidate.Name;
        return string.Equals(candidateName, normalizedTypeName, StringComparison.Ordinal) ||
               string.Equals(StripGenericArity(candidateName), normalizedTypeName, StringComparison.Ordinal);
    }

    private static string NormalizeRemoteTypeName(string typeName)
    {
        var normalized = typeName.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized["global::".Length..];
        }

        normalized = StripAssemblyQualification(normalized);
        return StripGenericArity(normalized);
    }

    private static string StripAssemblyQualification(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        var bracketDepth = 0;
        for (var index = 0; index < typeName.Length; index++)
        {
            var ch = typeName[index];
            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case ',' when bracketDepth == 0:
                    return typeName[..index].Trim();
            }
        }

        return typeName.Trim();
    }

    private static string StripGenericArity(string typeName)
    {
        var tickIndex = typeName.IndexOf('`');
        return tickIndex > 0
            ? typeName[..tickIndex]
            : typeName;
    }

    private static bool IsProcessedRemoteOperation(long operationId)
    {
        lock (Sync)
        {
            return ProcessedRemoteOperationIds.Contains(operationId);
        }
    }

    private static void MarkRemoteOperationProcessed(long operationId)
    {
        lock (Sync)
        {
            if (!ProcessedRemoteOperationIds.Add(operationId))
            {
                return;
            }

            ProcessedRemoteOperationOrder.Enqueue(operationId);
            while (ProcessedRemoteOperationOrder.Count > MaxProcessedRemoteOperationHistory)
            {
                var staleOperationId = ProcessedRemoteOperationOrder.Dequeue();
                ProcessedRemoteOperationIds.Remove(staleOperationId);
            }
        }
    }

    private static void TryPublishRemoteOperationResult(SourceGenHotReloadRemoteUpdateResult result)
    {
        ISourceGenHotReloadRemoteOperationTransport? remoteTransport;
        lock (Sync)
        {
            remoteTransport = ActiveRemoteOperationTransport;
        }

        if (remoteTransport is null)
        {
            return;
        }

        try
        {
            remoteTransport.PublishRemoteUpdateResult(result);
        }
        catch (Exception ex)
        {
            Trace("Failed to publish remote operation ACK: " + ex.Message);
        }
    }

    private static void CompleteMetadataHandshakeIfPending()
    {
        var shouldPublish = false;
        lock (Sync)
        {
            if (ActiveTransport is not MetadataUpdateTransport ||
                MetadataHandshakeCompleted)
            {
                return;
            }

            MetadataHandshakeCompleted = true;
            MetadataHandshakePending = false;
            StopMetadataHandshakeTimerLocked();
            shouldPublish = true;
        }

        if (shouldPublish)
        {
            PublishTransportStatus(
                SourceGenHotReloadTransportStatusKind.HandshakeCompleted,
                "MetadataUpdate",
                TransportMode,
                "Metadata transport handshake completed after first metadata delta.",
                isFallback: false);
        }
    }

    private static void ScheduleMetadataHandshakeTimeoutLocked()
    {
        if (MetadataHandshakeTimer is null)
        {
            MetadataHandshakeTimer = new Timer(static _ => OnMetadataHandshakeTimeout(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        MetadataHandshakeTimer.Change(HandshakeTimeout, Timeout.InfiniteTimeSpan);
    }

    private static void StopMetadataHandshakeTimerLocked()
    {
        if (MetadataHandshakeTimer is null)
        {
            return;
        }

        MetadataHandshakeTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        MetadataHandshakeTimer.Dispose();
        MetadataHandshakeTimer = null;
    }

    private static void OnMetadataHandshakeTimeout()
    {
        SourceGenHotReloadTransportMode mode;
        var shouldFallback = false;

        lock (Sync)
        {
            if (!MetadataHandshakePending ||
                ActiveTransport is not MetadataUpdateTransport ||
                MetadataHandshakeCompleted)
            {
                return;
            }

            MetadataHandshakePending = false;
            StopMetadataHandshakeTimerLocked();
            mode = TransportMode;
            shouldFallback = ShouldAttemptRemoteFallbackFromMetadataTimeoutLocked();
        }

        PublishTransportStatus(
            SourceGenHotReloadTransportStatusKind.HandshakeFailed,
            "MetadataUpdate",
            mode,
            "Metadata transport handshake timed out after " + HandshakeTimeout.TotalMilliseconds + "ms without receiving metadata delta.",
            isFallback: false);

        if (!shouldFallback)
        {
            return;
        }

        _ = TrySelectAndStartTransport(GetRemoteTransport(), mode, isFallback: true);
    }

    private static bool ShouldAttemptRemoteFallbackFromMetadataTimeoutLocked()
    {
        if (TransportMode != SourceGenHotReloadTransportMode.Auto)
        {
            return false;
        }

        return RemoteSocketTransport.HasConfiguredEndpointEnvironment() ||
               IsEnabledByEnvironment(IosHotReloadEnabledEnvVarName);
    }

    private static SourceGenHotReloadTransportMode ResolveTransportModeFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(TransportModeEnvVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return SourceGenHotReloadTransportMode.Auto;
        }

        if (Enum.TryParse<SourceGenHotReloadTransportMode>(value, ignoreCase: true, out var mode))
        {
            return mode;
        }

        Trace("Invalid transport mode '" + value + "'. Falling back to Auto.");
        return SourceGenHotReloadTransportMode.Auto;
    }

    private static TimeSpan ResolveHandshakeTimeoutFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(HandshakeTimeoutEnvVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromMilliseconds(DefaultHandshakeTimeoutMs);
        }

        if (!int.TryParse(value, out var timeoutMs) || timeoutMs <= 0)
        {
            Trace("Invalid handshake timeout '" + value + "'. Falling back to " + DefaultHandshakeTimeoutMs + "ms.");
            return TimeSpan.FromMilliseconds(DefaultHandshakeTimeoutMs);
        }

        return TimeSpan.FromMilliseconds(timeoutMs);
    }

    private static bool IsEnabledByEnvironment(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static void PublishTransportStatus(
        SourceGenHotReloadTransportStatusKind kind,
        string transportName,
        SourceGenHotReloadTransportMode mode,
        string message,
        bool isFallback,
        Exception? exception = null)
    {
        var status = new SourceGenHotReloadTransportStatus(
            kind,
            transportName,
            mode,
            message,
            DateTimeOffset.UtcNow,
            isFallback,
            exception);

        HotReloadTransportStatusChanged?.Invoke(status);
        XamlSourceGenHotReloadEventBus.Instance.PublishTransportStatusChanged(status);
        Trace(
            "Transport status: " + kind + ", transport=" + transportName +
            ", mode=" + mode + ", fallback=" + isFallback + ", message=" + message + ".");
    }

    private static void PublishRemoteOperationStatus(SourceGenHotReloadRemoteOperationStatus status)
    {
        HotReloadRemoteOperationStatusChanged?.Invoke(status);
        XamlSourceGenHotReloadEventBus.Instance.PublishRemoteOperationStatusChanged(status);

        var summary = "#" + status.OperationId +
                      " state=" + status.State +
                      ", requestId=" + (status.RequestId ?? "<null>") +
                      ", correlation=" + (status.CorrelationId?.ToString() ?? "<null>") +
                      ", diagnostics=" + (status.Diagnostics?.Count ?? 0) + ".";
        Trace("Remote operation status: " + summary);
    }

    private static string FormatTransportCapabilities(SourceGenHotReloadTransportCapabilities capabilities)
    {
        return "supported=" + capabilities.IsSupported +
               ", metadata=" + capabilities.SupportsMetadataUpdates +
               ", remote=" + capabilities.SupportsRemoteConnection +
               ", endpointRequired=" + capabilities.RequiresEndpointConfiguration +
               ", diagnostic=" + capabilities.Diagnostic;
    }

    private static void ResetTransportStateLocked()
    {
        RewireRemoteTransportSubscriptionLocked(null);
        StopMetadataHandshakeTimerLocked();
        MetadataHandshakePending = false;
        MetadataHandshakeCompleted = false;
        TransportInitialized = false;
        TransportMode = SourceGenHotReloadTransportMode.Auto;
        HandshakeTimeout = TimeSpan.FromMilliseconds(DefaultHandshakeTimeoutMs);
        ProcessedRemoteOperationIds.Clear();
        ProcessedRemoteOperationOrder.Clear();

        try
        {
            ActiveTransport?.Stop();
        }
        catch
        {
            // Best effort transport stop only.
        }

        try
        {
            MetadataTransport?.Stop();
        }
        catch
        {
            // Best effort transport stop only.
        }

        try
        {
            RemoteTransport?.Stop();
        }
        catch
        {
            // Best effort transport stop only.
        }

        ActiveTransport = null;
        MetadataTransport = null;
        RemoteTransport = null;
    }

    public static void ClearCache(Type[]? types)
    {
        lock (Sync)
        {
            if (types is null || types.Length == 0)
            {
                foreach (var references in Instances.Values)
                {
                    PruneDeadReferences(references);
                }

                return;
            }

            foreach (var type in types)
            {
                var normalizedType = NormalizeUpdatedType(type);
                if (Instances.TryGetValue(normalizedType, out var references))
                {
                    PruneDeadReferences(references);
                }
            }
        }
    }

    public static void UpdateApplication(Type[]? types)
    {
        UpdateApplicationCore(types, SourceGenHotReloadTrigger.MetadataUpdate);
    }

    private static void UpdateApplicationFromIdePolling(Type[] types)
    {
        UpdateApplicationCore(types, SourceGenHotReloadTrigger.IdePollingFallback);
    }

    private static void UpdateApplicationCore(
        Type[]? types,
        SourceGenHotReloadTrigger trigger,
        SourceGenHotReloadRemoteUpdateRequest? remoteRequest = null)
    {
        var eventBus = XamlSourceGenHotReloadEventBus.Instance;
        var normalizedTypes = NormalizeUpdatedTypes(types);
        if (ShouldSuppressDuplicateReloadRequest(trigger, normalizedTypes))
        {
            Trace("Suppressed duplicate hot reload request. Trigger: " + trigger + ", Requested types: " + FormatTypeList(normalizedTypes) + ".");
            return;
        }

        if (!IsEnabled)
        {
            HotReloaded?.Invoke(normalizedTypes);
            eventBus.PublishHotReloaded(normalizedTypes);
            return;
        }

        EnsureTransportInitialized();
        if (trigger == SourceGenHotReloadTrigger.MetadataUpdate)
        {
            CompleteMetadataHandshakeIfPending();
        }

        lock (Sync)
        {
            if (ReloadInProgress)
            {
                QueuePendingReloadLocked(normalizedTypes);
                Trace("Queued hot reload request while reload pipeline is already active.");
                return;
            }

            ReloadInProgress = true;
        }

        try
        {
            var currentTypes = normalizedTypes;
            var currentTrigger = trigger;
            var currentRemoteRequest = remoteRequest;
            while (true)
            {
                var refreshedTypes = RefreshArtifactsForUpdatedTypes(currentTypes);
                var operations = CollectReloadOperations(currentTypes);
                var context = BuildUpdateContext(currentTrigger, currentTypes, operations, currentRemoteRequest);
                Trace("UpdateApplication invoked. Trigger: " + currentTrigger + ". Candidate operations: " + operations.Count + ".");
                Trace("UpdateApplication requested types: " + FormatTypeList(currentTypes) + ".");
                if (refreshedTypes.Count > 0)
                {
                    Trace("UpdateApplication refreshed artifact registrations for: " + FormatTypeList(refreshedTypes.ToArray()) + ".");
                }
                Trace("UpdateApplication resolved targets: " + FormatOperationTargets(operations) + ".");

                ExecuteReloadPipeline(context, operations);
                HotReloaded?.Invoke(currentTypes);
                eventBus.PublishHotReloaded(currentTypes);
                HotReloadPipelineCompleted?.Invoke(context);
                eventBus.PublishPipelineCompleted(context);

                if (currentTrigger == SourceGenHotReloadTrigger.MetadataUpdate)
                {
                    TryDisableIdePollingFallbackAfterMetadataUpdate();
                }

                lock (Sync)
                {
                    if (!TryDequeuePendingReloadLocked(out currentTypes))
                    {
                        break;
                    }

                    currentTrigger = SourceGenHotReloadTrigger.Queued;
                    currentRemoteRequest = null;
                }
            }
        }
        finally
        {
            lock (Sync)
            {
                ReloadInProgress = false;
            }
        }
    }

    private static List<Type> RefreshArtifactsForUpdatedTypes(Type[]? types)
    {
        var refreshed = new List<Type>();
        if (types is null || types.Length == 0)
        {
            return refreshed;
        }

        var seen = new HashSet<Type>();
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            if (type is null)
            {
                continue;
            }

            var normalizedType = NormalizeType(type);
            if (!seen.Add(normalizedType))
            {
                continue;
            }

            if (!XamlSourceGenArtifactRefreshRegistry.TryRefresh(normalizedType))
            {
                continue;
            }

            refreshed.Add(normalizedType);
        }

        return refreshed;
    }

    private static void StartIdePollingTimerLocked()
    {
        if (IdePollingTimer is null)
        {
            IdePollingTimer = new Timer(static _ => PollIdeFallbackChanges(), null, IdePollingIntervalMs, IdePollingIntervalMs);
            return;
        }

        IdePollingTimer.Change(IdePollingIntervalMs, IdePollingIntervalMs);
    }

    private static void StopIdePollingTimerLocked()
    {
        if (IdePollingTimer is null)
        {
            return;
        }

        IdePollingTimer.Change(Timeout.Infinite, Timeout.Infinite);
        IdePollingTimer.Dispose();
        IdePollingTimer = null;
    }

    private static void DisableIdePollingFallbackLocked()
    {
        IsIdePollingFallbackEnabled = false;
        IdeSourcePathWatchers.Clear();
        StopIdePollingTimerLocked();
    }

    private static void PollIdeFallbackChanges()
    {
        Type[]? changedTypes = null;

        lock (Sync)
        {
            if (!IsIdePollingFallbackEnabled || IdeSourcePathWatchers.Count == 0)
            {
                return;
            }

            var changed = new HashSet<Type>();
            var toRemove = new List<Type>();

            foreach (var pair in IdeSourcePathWatchers)
            {
                var type = pair.Key;

                if (!Instances.ContainsKey(type))
                {
                    toRemove.Add(type);
                    continue;
                }

                var sourcePathState = pair.Value;
                if (sourcePathState.TryConsumeReloadSignal())
                {
                    changed.Add(type);
                }
            }

            foreach (var type in toRemove)
            {
                IdeSourcePathWatchers.Remove(type);
            }

            if (changed.Count == 0)
            {
                return;
            }

            changedTypes = new Type[changed.Count];
            changed.CopyTo(changedTypes);
        }

        if (changedTypes is { Length: > 0 })
        {
            Trace("IDE polling detected changed types: " + string.Join(", ", changedTypes));
            UpdateApplicationFromIdePolling(changedTypes);
        }
    }

    private static string? NormalizeSourcePath(string sourcePath)
    {
        try
        {
            return Path.GetFullPath(sourcePath.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNormalizeBuildUri(string? buildUri, out string normalizedBuildUri)
    {
        normalizedBuildUri = UriMapper.Normalize(buildUri);
        return !string.IsNullOrWhiteSpace(normalizedBuildUri);
    }

    private static Type[]? NormalizeUpdatedTypes(Type[]? types)
    {
        if (types is null || types.Length == 0)
        {
            return null;
        }

        var normalized = new HashSet<Type>();
        foreach (var type in types)
        {
            if (type is null)
            {
                continue;
            }

            normalized.Add(NormalizeUpdatedType(type));
        }

        if (normalized.Count == 0)
        {
            return Array.Empty<Type>();
        }

        var result = new Type[normalized.Count];
        normalized.CopyTo(result);
        return result;
    }

    private static bool ShouldSuppressDuplicateReloadRequest(SourceGenHotReloadTrigger trigger, Type[]? normalizedTypes)
    {
        if (trigger == SourceGenHotReloadTrigger.Queued ||
            trigger == SourceGenHotReloadTrigger.RemoteTransport)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        lock (Sync)
        {
            if (ReloadInProgress)
            {
                return false;
            }

            var dedupKey = BuildReloadDedupKey(normalizedTypes);
            if (string.IsNullOrWhiteSpace(dedupKey))
            {
                return false;
            }

            if (LastAcceptedReloadKey is null || LastAcceptedReloadTrigger is null)
            {
                LastAcceptedReloadKey = dedupKey;
                LastAcceptedReloadTrigger = trigger;
                LastAcceptedReloadTimestampUtc = now;
                return false;
            }

            if (!string.Equals(LastAcceptedReloadKey, dedupKey, StringComparison.Ordinal))
            {
                LastAcceptedReloadKey = dedupKey;
                LastAcceptedReloadTrigger = trigger;
                LastAcceptedReloadTimestampUtc = now;
                return false;
            }

            var elapsed = now - LastAcceptedReloadTimestampUtc;
            var previousTrigger = LastAcceptedReloadTrigger.Value;
            if (elapsed <= DuplicateReloadWindow &&
                previousTrigger == trigger)
            {
                return true;
            }

            if (elapsed <= MetadataVsPollingDedupWindow &&
                ((previousTrigger == SourceGenHotReloadTrigger.MetadataUpdate &&
                  trigger == SourceGenHotReloadTrigger.IdePollingFallback) ||
                 (previousTrigger == SourceGenHotReloadTrigger.IdePollingFallback &&
                  trigger == SourceGenHotReloadTrigger.MetadataUpdate)))
            {
                return true;
            }

            LastAcceptedReloadKey = dedupKey;
            LastAcceptedReloadTrigger = trigger;
            LastAcceptedReloadTimestampUtc = now;
            return false;
        }
    }

    private static string BuildReloadDedupKey(Type[]? normalizedTypes)
    {
        var typeKeys = new SortedSet<string>(StringComparer.Ordinal);
        var uriKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (normalizedTypes is { Length: > 0 })
        {
            for (var index = 0; index < normalizedTypes.Length; index++)
            {
                var type = normalizedTypes[index];
                if (type is null)
                {
                    continue;
                }

                if (TryGetBuildUriForTypeOrDeclaringLocked(type, out var buildUri) &&
                    !string.IsNullOrWhiteSpace(buildUri))
                {
                    uriKeys.Add(buildUri);
                    continue;
                }

                var fullName = type.FullName;
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    typeKeys.Add(fullName);
                }
            }
        }

        if (typeKeys.Count == 0 && uriKeys.Count == 0)
        {
            return "<all>";
        }

        // When at least one build URI is known, dedupe by URI only. Generated nested helper
        // type names can vary between deltas for the same XAML change.
        if (uriKeys.Count > 0)
        {
            return ":" + string.Join("|", uriKeys);
        }

        var typePart = typeKeys.Count == 0
            ? string.Empty
            : string.Join("|", typeKeys);
        return typePart + ":";
    }

    private static void TryDisableIdePollingFallbackAfterMetadataUpdate()
    {
        var disabled = false;
        lock (Sync)
        {
            if (!IsIdePollingFallbackEnabled)
            {
                return;
            }

            DisableIdePollingFallbackLocked();
            disabled = true;
        }

        if (disabled)
        {
            Trace("Disabled IDE polling fallback after first metadata update in current session.");
        }
    }

    private static Type NormalizeUpdatedType(Type type)
    {
        var normalizedType = NormalizeType(type);

        lock (Sync)
        {
            if (ReplacementTypeMap.TryGetValue(normalizedType, out var mappedType))
            {
                return mappedType;
            }

            foreach (var trackedType in Instances.Keys)
            {
                if (!string.Equals(trackedType.FullName, normalizedType.FullName, StringComparison.Ordinal))
                {
                    continue;
                }

                ReplacementTypeMap[normalizedType] = trackedType;
                ReplacementTypeMap[trackedType] = trackedType;
                return trackedType;
            }

            ReplacementTypeMap[normalizedType] = normalizedType;
            return normalizedType;
        }
    }

    private static List<ReloadOperation> CollectReloadOperations(Type[]? types)
    {
        lock (Sync)
        {
            var operations = new List<ReloadOperation>();

            if (types is null || types.Length == 0)
            {
                foreach (var pair in Instances)
                {
                    if (!Registrations.TryGetValue(pair.Key, out var registration))
                    {
                        continue;
                    }

                    AddOperationsForType(pair.Key, pair.Value, registration, operations);
                }

                return operations;
            }

            var reloadTypes = ResolveRequestedReloadTypesLocked(types);
            foreach (var reloadType in reloadTypes)
            {
                if (!Instances.TryGetValue(reloadType, out var references))
                {
                    continue;
                }

                if (!Registrations.TryGetValue(reloadType, out var registration))
                {
                    continue;
                }

                AddOperationsForType(reloadType, references, registration, operations);
            }

            return operations;
        }
    }

    private static IReadOnlyCollection<Type> ResolveRequestedReloadTypesLocked(Type[] types)
    {
        var reloadTypes = new HashSet<Type>();
        var affectedBuildUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requestedType in types)
        {
            var normalizedType = NormalizeType(requestedType);
            if (TryResolveTrackedTypeLocked(normalizedType, out var trackedType))
            {
                reloadTypes.Add(trackedType);
            }

            if (TryGetBuildUriForTypeOrDeclaringLocked(normalizedType, out var buildUri))
            {
                affectedBuildUris.Add(buildUri);
            }
        }

        if (affectedBuildUris.Count == 0)
        {
            return reloadTypes;
        }

        ExpandIncomingBuildUrisLocked(affectedBuildUris);
        foreach (var pair in BuildUrisByType)
        {
            if (!affectedBuildUris.Contains(pair.Value))
            {
                continue;
            }

            reloadTypes.Add(pair.Key);
        }

        return reloadTypes;
    }

    private static bool TryGetBuildUriForTypeOrDeclaringLocked(Type normalizedType, out string buildUri)
    {
        var current = normalizedType;
        while (current is not null)
        {
            if (TryGetBuildUriForTypeLocked(current, out buildUri) &&
                !string.IsNullOrWhiteSpace(buildUri))
            {
                return true;
            }

            var declaring = current.DeclaringType;
            current = declaring is null ? null : NormalizeType(declaring);
        }

        buildUri = string.Empty;
        return false;
    }

    private static bool TryGetBuildUriForTypeLocked(Type normalizedType, out string buildUri)
    {
        if (BuildUrisByType.TryGetValue(normalizedType, out buildUri!))
        {
            return true;
        }

        if (ReplacementTypeMap.TryGetValue(normalizedType, out var mappedType) &&
            BuildUrisByType.TryGetValue(mappedType, out buildUri!))
        {
            return true;
        }

        if (XamlSourceGenTypeUriRegistry.TryGetUri(normalizedType, out buildUri!))
        {
            buildUri = UriMapper.Normalize(buildUri);
            return !string.IsNullOrWhiteSpace(buildUri);
        }

        if (ReplacementTypeMap.TryGetValue(normalizedType, out mappedType) &&
            XamlSourceGenTypeUriRegistry.TryGetUri(mappedType, out buildUri!))
        {
            buildUri = UriMapper.Normalize(buildUri);
            return !string.IsNullOrWhiteSpace(buildUri);
        }

        buildUri = string.Empty;
        return false;
    }

    private static void ExpandIncomingBuildUrisLocked(HashSet<string> affectedBuildUris)
    {
        var queue = new Queue<string>();
        foreach (var buildUri in affectedBuildUris)
        {
            queue.Enqueue(buildUri);
        }

        while (queue.Count > 0)
        {
            var currentBuildUri = queue.Dequeue();
            foreach (var incoming in XamlIncludeGraphRegistry.GetIncoming(currentBuildUri))
            {
                var sourceUri = UriMapper.Normalize(incoming.SourceUri);
                if (string.IsNullOrWhiteSpace(sourceUri) ||
                    !affectedBuildUris.Add(sourceUri))
                {
                    continue;
                }

                queue.Enqueue(sourceUri);
            }
        }
    }

    private static bool TryResolveTrackedTypeLocked(Type normalizedType, out Type trackedType)
    {
        trackedType = normalizedType;
        if (Instances.ContainsKey(normalizedType))
        {
            return true;
        }

        if (ReplacementTypeMap.TryGetValue(normalizedType, out var mappedType) &&
            Instances.ContainsKey(mappedType))
        {
            trackedType = mappedType;
            return true;
        }

        return false;
    }

    private static void AddOperationsForType(
        Type type,
        List<WeakReference<object>> references,
        ReloadRegistration registration,
        List<ReloadOperation> operations)
    {
        for (var index = references.Count - 1; index >= 0; index--)
        {
            if (!references[index].TryGetTarget(out var instance) || instance is null)
            {
                references.RemoveAt(index);
                continue;
            }

            operations.Add(new ReloadOperation(type, instance, registration));
        }
    }

    private static SourceGenHotReloadUpdateContext BuildUpdateContext(
        SourceGenHotReloadTrigger trigger,
        Type[]? requestedTypes,
        List<ReloadOperation> operations,
        SourceGenHotReloadRemoteUpdateRequest? remoteRequest = null)
    {
        var uniqueReloadedTypes = new List<Type>();
        var seen = new HashSet<Type>();
        foreach (var operation in operations)
        {
            if (seen.Add(operation.Type))
            {
                uniqueReloadedTypes.Add(operation.Type);
            }
        }

        return new SourceGenHotReloadUpdateContext(
            trigger,
            requestedTypes,
            uniqueReloadedTypes,
            operations.Count,
            operationId: remoteRequest?.OperationId,
            requestId: remoteRequest?.RequestId,
            correlationId: remoteRequest?.CorrelationId);
    }

    private static void ExecuteReloadPipeline(
        SourceGenHotReloadUpdateContext context,
        List<ReloadOperation> operations)
    {
        var handlers = SnapshotHandlers();
        HotReloadPipelineStarted?.Invoke(context);
        XamlSourceGenHotReloadEventBus.Instance.PublishPipelineStarted(context);

        void RunPipeline()
        {
            var previousSuppressStateTransfer = SuppressStatefulControlTreeStateTransfer;
            SuppressStatefulControlTreeStateTransfer = ShouldSuppressStatefulControlTreeStateTransfer(operations);
            try
            {
                InvokeBeforeVisualTreeUpdate(context, handlers);
                foreach (var operation in operations)
                {
                    ExecuteReload(operation, handlers, context.Trigger);
                }

                InvokeAfterVisualTreeUpdate(context, handlers);
                InvokeReloadCompleted(context, handlers);
            }
            finally
            {
                SuppressStatefulControlTreeStateTransfer = previousSuppressStateTransfer;
            }
        }

        if (!NeedsUiDispatch(operations))
        {
            RunPipeline();
            return;
        }

        try
        {
            var uiThread = Dispatcher.UIThread;
            if (uiThread.CheckAccess())
            {
                RunPipeline();
                return;
            }

            uiThread.InvokeAsync(RunPipeline, DispatcherPriority.Background).GetAwaiter().GetResult();
        }
        catch
        {
            RunPipeline();
        }
    }

    private static bool NeedsUiDispatch(List<ReloadOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (RequiresUiDispatchForInstance(operation.Instance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldSuppressStatefulControlTreeStateTransfer(List<ReloadOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (operation.Instance is global::Avalonia.Styling.IStyle ||
                operation.Instance is global::Avalonia.Controls.IResourceProvider)
            {
                Trace("Suppressing control-tree state transfer for style/resource hot reload pipeline.");
                return true;
            }

            var operationType = operation.Type;
            if (typeof(global::Avalonia.Styling.IStyle).IsAssignableFrom(operationType) ||
                typeof(global::Avalonia.Controls.IResourceProvider).IsAssignableFrom(operationType))
            {
                Trace("Suppressing control-tree state transfer for style/resource hot reload pipeline.");
                return true;
            }
        }

        return false;
    }

    internal static bool RequiresUiDispatchForInstance(object? instance)
    {
        if (instance is null)
        {
            return false;
        }

        if (instance is global::Avalonia.AvaloniaObject)
        {
            return true;
        }

        // Style/resource hosts (for example FluentTheme) also mutate runtime visual behavior
        // and must be reloaded on the UI thread.
        return instance is global::Avalonia.Styling.IStyle ||
               instance is global::Avalonia.Controls.IResourceProvider;
    }

    private static IReadOnlyList<RegisteredHandler> SnapshotHandlers()
    {
        lock (Sync)
        {
            return Handlers.ToArray();
        }
    }

    private static void InvokeBeforeVisualTreeUpdate(
        SourceGenHotReloadUpdateContext context,
        IReadOnlyList<RegisteredHandler> handlers)
    {
        foreach (var registeredHandler in handlers)
        {
            try
            {
                registeredHandler.Handler.BeforeVisualTreeUpdate(context);
            }
            catch (Exception ex)
            {
                ReportHandlerFailure(typeof(object), "BeforeVisualTreeUpdate", ex);
            }
        }
    }

    private static void InvokeAfterVisualTreeUpdate(
        SourceGenHotReloadUpdateContext context,
        IReadOnlyList<RegisteredHandler> handlers)
    {
        foreach (var registeredHandler in handlers)
        {
            try
            {
                registeredHandler.Handler.AfterVisualTreeUpdate(context);
            }
            catch (Exception ex)
            {
                ReportHandlerFailure(typeof(object), "AfterVisualTreeUpdate", ex);
            }
        }
    }

    private static void InvokeReloadCompleted(
        SourceGenHotReloadUpdateContext context,
        IReadOnlyList<RegisteredHandler> handlers)
    {
        foreach (var registeredHandler in handlers)
        {
            try
            {
                registeredHandler.Handler.ReloadCompleted(context);
            }
            catch (Exception ex)
            {
                ReportHandlerFailure(typeof(object), "ReloadCompleted", ex);
            }
        }
    }

    private static void ExecuteReload(
        ReloadOperation operation,
        IReadOnlyList<RegisteredHandler> handlers,
        SourceGenHotReloadTrigger trigger)
    {
        Trace("Applying reload operation. Target type: " + operation.Type.FullName + ", instance type: " + operation.Instance.GetType().FullName + ".");
        var applicableHandlers = ResolveApplicableHandlers(operation, handlers);
        var capturedState = TryCaptureRegistrationState(operation);
        var handlerStates = CaptureHandlerStates(operation, applicableHandlers);

        try
        {
            foreach (var handlerState in handlerStates)
            {
                try
                {
                    handlerState.Handler.BeforeElementReload(operation.Type, operation.Instance, handlerState.State);
                }
                catch (Exception ex)
                {
                    ReportHandlerFailure(operation.Type, "BeforeElementReload", ex);
                }
            }

            operation.Registration.BeforeReload?.Invoke(operation.Instance);
            if (!TryApplyRuntimeSourceReload(operation, trigger))
            {
                operation.Registration.ReloadAction(operation.Instance);
            }
            Trace("Reload operation applied successfully for type '" + operation.Type.FullName + "'.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"XAML source generator hot reload failed for '{operation.Type.FullName}': {ex}");
            Trace("Reload operation failed for type '" + operation.Type.FullName + "': " + ex.Message);
            HotReloadFailed?.Invoke(operation.Type, ex);
            XamlSourceGenHotReloadEventBus.Instance.PublishHotReloadFailed(operation.Type, ex);
            if (IsRudeEditException(ex))
            {
                Trace("Detected rude-edit constrained hot reload failure for type '" + operation.Type.FullName + "'. A rebuild/restart may be required.");
                HotReloadRudeEditDetected?.Invoke(operation.Type, ex);
                XamlSourceGenHotReloadEventBus.Instance.PublishHotReloadRudeEditDetected(operation.Type, ex);
            }
        }
        finally
        {
            TryRestoreRegistrationState(operation, capturedState);
            TryInvokeAfterReloadCallback(operation);

            foreach (var handlerState in handlerStates)
            {
                try
                {
                    handlerState.Handler.AfterElementReload(operation.Type, operation.Instance, handlerState.State);
                }
                catch (Exception ex)
                {
                    ReportHandlerFailure(operation.Type, "AfterElementReload", ex);
                }
            }
        }
    }

    private static bool TryApplyRuntimeSourceReload(ReloadOperation operation, SourceGenHotReloadTrigger trigger)
    {
        if (trigger != SourceGenHotReloadTrigger.IdePollingFallback)
        {
            return false;
        }

        if (!TryGetRegisteredSourcePath(operation.Type, out var sourcePath) ||
            string.IsNullOrWhiteSpace(sourcePath) ||
            !File.Exists(sourcePath))
        {
            return false;
        }

        string xamlText;
        try
        {
            xamlText = File.ReadAllText(sourcePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Trace("Runtime source reload skipped for type '" + operation.Type.FullName + "': unable to read '" + sourcePath + "' (" + ex.Message + ").");
            return false;
        }

        try
        {
            var baseUri = TryCreateBaseUriForType(operation.Type);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xamlText));
            var document = new RuntimeXamlLoaderDocument(baseUri, operation.Instance, stream);
            var configuration = new RuntimeXamlLoaderConfiguration
            {
                LocalAssembly = operation.Instance.GetType().Assembly
            };

            var options = AvaloniaSourceGeneratedXamlLoader.RuntimeCompilationOptions;
            options.EnableRuntimeCompilationFallback = true;
            _ = SourceGenRuntimeXamlCompiler.Load(document, configuration, options);
            Trace("Applied runtime source reload for type '" + operation.Type.FullName + "' from '" + sourcePath + "'.");
            return true;
        }
        catch (Exception ex)
        {
            Trace("Runtime source reload failed for type '" + operation.Type.FullName + "' from '" + sourcePath + "': " + ex.Message + ". Falling back to generated reload path.");
            return false;
        }
    }

    private static bool TryGetRegisteredSourcePath(Type type, out string sourcePath)
    {
        lock (Sync)
        {
            if (TryGetRegisteredSourcePathLocked(type, out sourcePath))
            {
                return true;
            }

            var normalizedType = NormalizeType(type);
            return TryGetRegisteredSourcePathLocked(normalizedType, out sourcePath);
        }
    }

    private static bool TryGetRegisteredSourcePathLocked(Type type, out string sourcePath)
    {
        if (IdeSourcePathWatchers.TryGetValue(type, out var state))
        {
            sourcePath = state.SourcePath;
            return true;
        }

        if (ReplacementTypeMap.TryGetValue(type, out var mappedType) &&
            IdeSourcePathWatchers.TryGetValue(mappedType, out state))
        {
            sourcePath = state.SourcePath;
            return true;
        }

        sourcePath = string.Empty;
        return false;
    }

    private static Uri? TryCreateBaseUriForType(Type type)
    {
        lock (Sync)
        {
            if (!TryGetBuildUriForTypeOrDeclaringLocked(type, out var buildUri) ||
                string.IsNullOrWhiteSpace(buildUri))
            {
                return null;
            }

            return Uri.TryCreate(buildUri, UriKind.Absolute, out var baseUri) ? baseUri : null;
        }
    }

    private static IReadOnlyList<RegisteredHandler> ResolveApplicableHandlers(
        ReloadOperation operation,
        IReadOnlyList<RegisteredHandler> handlers)
    {
        if (handlers.Count == 0)
        {
            return Array.Empty<RegisteredHandler>();
        }

        var matches = new List<RegisteredHandler>();
        foreach (var registeredHandler in handlers)
        {
            if (registeredHandler.ElementType is not null &&
                !registeredHandler.ElementType.IsInstanceOfType(operation.Instance))
            {
                continue;
            }

            var canHandle = false;
            try
            {
                canHandle = registeredHandler.Handler.CanHandle(operation.Type, operation.Instance);
            }
            catch (Exception ex)
            {
                ReportHandlerFailure(operation.Type, "CanHandle", ex);
            }

            if (canHandle)
            {
                matches.Add(registeredHandler);
            }
        }

        return matches;
    }

    private static object? TryCaptureRegistrationState(ReloadOperation operation)
    {
        try
        {
            return operation.Registration.CaptureState?.Invoke(operation.Instance);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure(operation.Type, "CaptureState", ex);
            return null;
        }
    }

    private static void TryRestoreRegistrationState(ReloadOperation operation, object? capturedState)
    {
        try
        {
            operation.Registration.RestoreState?.Invoke(operation.Instance, capturedState);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure(operation.Type, "RestoreState", ex);
        }
    }

    private static void TryInvokeAfterReloadCallback(ReloadOperation operation)
    {
        try
        {
            operation.Registration.AfterReload?.Invoke(operation.Instance);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure(operation.Type, "AfterReload", ex);
        }
    }

    private static List<HandlerState> CaptureHandlerStates(
        ReloadOperation operation,
        IReadOnlyList<RegisteredHandler> handlers)
    {
        var states = new List<HandlerState>(handlers.Count);
        foreach (var registeredHandler in handlers)
        {
            object? state = null;
            try
            {
                state = registeredHandler.Handler.CaptureState(operation.Type, operation.Instance);
            }
            catch (Exception ex)
            {
                ReportHandlerFailure(operation.Type, "CaptureHandlerState", ex);
            }

            states.Add(new HandlerState(registeredHandler.Handler, state));
        }

        return states;
    }

    private static void ReportHandlerFailure(Type type, string phase, Exception exception)
    {
        HotReloadHandlerFailed?.Invoke(type, phase, exception);
        XamlSourceGenHotReloadEventBus.Instance.PublishHotReloadHandlerFailed(type, phase, exception);
        Trace("Hot reload handler failed in phase '" + phase + "' for type '" + type.FullName + "': " + exception.Message);
    }

    private static void QueuePendingReloadLocked(Type[]? types)
    {
        if (types is null || types.Length == 0)
        {
            PendingReloadAllTypes = true;
            PendingReloadTypes.Clear();
            return;
        }

        if (PendingReloadAllTypes)
        {
            return;
        }

        foreach (var type in types)
        {
            PendingReloadTypes.Add(type);
        }
    }

    private static bool TryDequeuePendingReloadLocked(out Type[]? types)
    {
        if (PendingReloadAllTypes)
        {
            PendingReloadAllTypes = false;
            PendingReloadTypes.Clear();
            types = null;
            return true;
        }

        if (PendingReloadTypes.Count == 0)
        {
            types = null;
            return false;
        }

        types = new Type[PendingReloadTypes.Count];
        PendingReloadTypes.CopyTo(types);
        PendingReloadTypes.Clear();
        return true;
    }

    private static void AddDefaultHandlersLocked()
    {
        AddHandlerLocked(new StatefulControlTreeHotReloadHandler(), typeof(global::Avalonia.LogicalTree.ILogical), "default");
        AddHandlerLocked(new StyledElementDataContextHotReloadHandler(), typeof(global::Avalonia.StyledElement), "default");
        AddHandlerLocked(new StyleHostVisualRefreshHotReloadHandler(), typeof(global::Avalonia.Styling.IStyle), "default");
    }

    private static void AddHandlerLocked(
        ISourceGenHotReloadHandler handler,
        Type? elementType,
        string sourceKey)
    {
        var key = sourceKey + "|" +
                  handler.GetType().AssemblyQualifiedName + "|" +
                  (elementType?.AssemblyQualifiedName ?? "*");
        if (!HandlerKeys.Add(key))
        {
            return;
        }

        Handlers.Add(new RegisteredHandler(handler, elementType));
        Handlers.Sort(static (left, right) =>
        {
            var priorityCompare = right.Handler.Priority.CompareTo(left.Handler.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            var leftName = left.Handler.GetType().FullName ?? string.Empty;
            var rightName = right.Handler.GetType().FullName ?? string.Empty;
            return string.Compare(leftName, rightName, StringComparison.Ordinal);
        });
    }

    private static Type NormalizeType(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }

    private static bool ContainsReference(List<WeakReference<object>> references, object candidate)
    {
        foreach (var reference in references)
        {
            if (reference.TryGetTarget(out var current) &&
                current is not null &&
                ReferenceEquals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static void PruneDeadReferences(List<WeakReference<object>> references)
    {
        for (var index = references.Count - 1; index >= 0; index--)
        {
            if (!references[index].TryGetTarget(out _))
            {
                references.RemoveAt(index);
            }
        }
    }

    private readonly record struct ReloadRegistration(
        Action<object> ReloadAction,
        Action<object>? BeforeReload,
        Func<object, object?>? CaptureState,
        Action<object, object?>? RestoreState,
        Action<object>? AfterReload);

    private readonly record struct ReloadOperation(Type Type, object Instance, ReloadRegistration Registration);

    private sealed class RegisteredHandler(ISourceGenHotReloadHandler handler, Type? elementType)
    {
        public ISourceGenHotReloadHandler Handler { get; } = handler;

        public Type? ElementType { get; } = elementType;
    }

    private sealed class HandlerState(ISourceGenHotReloadHandler handler, object? state)
    {
        public ISourceGenHotReloadHandler Handler { get; } = handler;

        public object? State { get; } = state;
    }

    private sealed class SourcePathWatchState
    {
        private readonly string _sourcePath;
        private long _lastWriteTicks;
        private int _remainingReloadAttempts;

        private SourcePathWatchState(string sourcePath, long lastWriteTicks)
        {
            _sourcePath = sourcePath;
            _lastWriteTicks = lastWriteTicks;
        }

        public static SourcePathWatchState Create(string sourcePath)
        {
            return new SourcePathWatchState(sourcePath, ReadLastWriteTicks(sourcePath));
        }

        public string SourcePath => _sourcePath;

        public bool TryConsumeReloadSignal()
        {
            var currentTicks = ReadLastWriteTicks(_sourcePath);
            if (currentTicks > 0 && currentTicks != _lastWriteTicks)
            {
                _lastWriteTicks = currentTicks;
                _remainingReloadAttempts = SourcePathReloadRetryCount;
                Trace("Source change detected at '" + _sourcePath + "'. Scheduling " + SourcePathReloadRetryCount + " reload attempts.");
            }

            if (_remainingReloadAttempts <= 0)
            {
                return false;
            }

            _remainingReloadAttempts--;
            return true;
        }

        private static long ReadLastWriteTicks(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return 0;
                }

                return File.GetLastWriteTimeUtc(sourcePath).Ticks;
            }
            catch
            {
                return 0;
            }
        }
    }

    private sealed class StyledElementDataContextHotReloadHandler : ISourceGenHotReloadHandler
    {
        public int Priority => -100;

        public bool CanHandle(Type reloadType, object instance)
        {
            return instance is global::Avalonia.StyledElement;
        }

        public object? CaptureState(Type reloadType, object instance)
        {
            if (instance is not global::Avalonia.StyledElement styledElement)
            {
                return null;
            }

            return new StyledElementState(styledElement.DataContext);
        }

        public void AfterElementReload(Type reloadType, object instance, object? state)
        {
            if (instance is not global::Avalonia.StyledElement styledElement ||
                state is not StyledElementState styledElementState)
            {
                return;
            }

            if (styledElement.DataContext is null && styledElementState.DataContext is not null)
            {
                styledElement.DataContext = styledElementState.DataContext;
            }
        }

        private sealed class StyledElementState(object? dataContext)
        {
            public object? DataContext { get; } = dataContext;
        }
    }

    private sealed class StatefulControlTreeHotReloadHandler : ISourceGenHotReloadHandler
    {
        public int Priority => 200;

        public bool CanHandle(Type reloadType, object instance)
        {
            return instance is global::Avalonia.LogicalTree.ILogical;
        }

        public object? CaptureState(Type reloadType, object instance)
        {
            if (SuppressStatefulControlTreeStateTransfer)
            {
                return null;
            }

            if (instance is not global::Avalonia.LogicalTree.ILogical logicalRoot)
            {
                return null;
            }

            return ControlTreeStateSnapshot.Capture(logicalRoot);
        }

        public void AfterElementReload(Type reloadType, object instance, object? state)
        {
            if (SuppressStatefulControlTreeStateTransfer)
            {
                return;
            }

            if (instance is not global::Avalonia.LogicalTree.ILogical logicalRoot ||
                state is not ControlTreeStateSnapshot snapshot)
            {
                return;
            }

            snapshot.Restore(logicalRoot);
        }

        private sealed class ControlTreeStateSnapshot
        {
            private readonly IReadOnlyList<CapturedControlState> _states;

            private ControlTreeStateSnapshot(IReadOnlyList<CapturedControlState> states)
            {
                _states = states;
            }

            public static ControlTreeStateSnapshot Capture(global::Avalonia.LogicalTree.ILogical root)
            {
                var states = new List<CapturedControlState>(32);
                CaptureNode(root, "root", states);
                return new ControlTreeStateSnapshot(states);
            }

            public void Restore(global::Avalonia.LogicalTree.ILogical root)
            {
                if (_states.Count == 0)
                {
                    return;
                }

                var liveControls = new List<LiveControlTarget>(_states.Count * 2);
                BuildControlLookup(root, "root", liveControls);
                if (liveControls.Count == 0)
                {
                    return;
                }

                var stateMatched = new bool[_states.Count];
                var liveMatched = new bool[liveControls.Count];

                // First pass: deterministic identity matches.
                for (var stateIndex = 0; stateIndex < _states.Count; stateIndex++)
                {
                    var captured = _states[stateIndex];
                    if (captured.Name is not null &&
                        TryMatch(
                            captured,
                            liveControls,
                            liveMatched,
                            static (state, target) => string.Equals(state.Name, target.Name, StringComparison.Ordinal),
                            allowSignatureFallback: true))
                    {
                        stateMatched[stateIndex] = true;
                    }
                }

                for (var stateIndex = 0; stateIndex < _states.Count; stateIndex++)
                {
                    if (stateMatched[stateIndex])
                    {
                        continue;
                    }

                    var captured = _states[stateIndex];
                    if (TryMatch(
                            captured,
                            liveControls,
                            liveMatched,
                            static (state, target) => string.Equals(state.Path, target.Path, StringComparison.Ordinal),
                            allowSignatureFallback: false))
                    {
                        stateMatched[stateIndex] = true;
                    }
                }

                // Second pass: semantic signature matches for surviving controls after sibling edits.
                var unmatchedStateBySignature = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                var unmatchedLiveBySignature = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                for (var stateIndex = 0; stateIndex < _states.Count; stateIndex++)
                {
                    if (stateMatched[stateIndex])
                    {
                        continue;
                    }

                    var key = BuildSignatureKey(_states[stateIndex].ControlType, _states[stateIndex].Signature);
                    if (!unmatchedStateBySignature.TryGetValue(key, out var stateIndexes))
                    {
                        stateIndexes = new List<int>();
                        unmatchedStateBySignature.Add(key, stateIndexes);
                    }

                    stateIndexes.Add(stateIndex);
                }

                for (var liveIndex = 0; liveIndex < liveControls.Count; liveIndex++)
                {
                    if (liveMatched[liveIndex])
                    {
                        continue;
                    }

                    var live = liveControls[liveIndex];
                    var key = BuildSignatureKey(live.Control.GetType(), live.Signature);
                    if (!unmatchedLiveBySignature.TryGetValue(key, out var liveIndexes))
                    {
                        liveIndexes = new List<int>();
                        unmatchedLiveBySignature.Add(key, liveIndexes);
                    }

                    liveIndexes.Add(liveIndex);
                }

                foreach (var pair in unmatchedStateBySignature)
                {
                    if (!unmatchedLiveBySignature.TryGetValue(pair.Key, out var liveIndexes) ||
                        pair.Value.Count == 0 ||
                        liveIndexes.Count == 0 ||
                        pair.Value.Count != liveIndexes.Count)
                    {
                        continue;
                    }

                    pair.Value.Sort(static (left, right) => left.CompareTo(right));
                    liveIndexes.Sort(static (left, right) => left.CompareTo(right));

                    for (var index = 0; index < pair.Value.Count; index++)
                    {
                        var stateIndex = pair.Value[index];
                        var liveIndex = liveIndexes[index];
                        if (stateMatched[stateIndex] || liveMatched[liveIndex])
                        {
                            continue;
                        }

                        _states[stateIndex].State.Restore(liveControls[liveIndex].Control);
                        stateMatched[stateIndex] = true;
                        liveMatched[liveIndex] = true;
                    }
                }
            }

            private static bool TryMatch(
                CapturedControlState state,
                IReadOnlyList<LiveControlTarget> liveControls,
                bool[] liveMatched,
                Func<CapturedControlState, LiveControlTarget, bool> predicate,
                bool allowSignatureFallback)
            {
                var bestMatchIndex = -1;
                for (var liveIndex = 0; liveIndex < liveControls.Count; liveIndex++)
                {
                    if (liveMatched[liveIndex])
                    {
                        continue;
                    }

                    var live = liveControls[liveIndex];
                    if (live.Control.GetType() != state.ControlType ||
                        !predicate(state, live))
                    {
                        continue;
                    }

                    if (!string.Equals(state.Signature, live.Signature, StringComparison.Ordinal))
                    {
                        if (!allowSignatureFallback || bestMatchIndex >= 0)
                        {
                            continue;
                        }

                        bestMatchIndex = liveIndex;
                        continue;
                    }

                    bestMatchIndex = liveIndex;
                    break;
                }

                if (bestMatchIndex < 0)
                {
                    return false;
                }

                state.State.Restore(liveControls[bestMatchIndex].Control);
                liveMatched[bestMatchIndex] = true;
                return true;
            }

            private static string BuildSignatureKey(Type type, string signature)
            {
                return type.AssemblyQualifiedName + "|" + signature;
            }

            private static void CaptureNode(
                global::Avalonia.LogicalTree.ILogical node,
                string path,
                List<CapturedControlState> states)
            {
                if (node is global::Avalonia.Controls.Control control)
                {
                    var state = TransientLocalValueStateSnapshot.Capture(control);
                    if (!state.IsEmpty)
                    {
                        states.Add(new CapturedControlState(
                            path,
                            TryGetNormalizedName(control),
                            BuildControlSemanticSignature(control),
                            control.GetType(),
                            state));
                    }
                }
                var ordinalByType = new Dictionary<Type, int>();
                foreach (var child in node.LogicalChildren)
                {
                    if (child is not global::Avalonia.LogicalTree.ILogical logicalChild)
                    {
                        continue;
                    }

                    var childType = logicalChild.GetType();
                    var ordinal = ordinalByType.TryGetValue(childType, out var currentOrdinal)
                        ? currentOrdinal + 1
                        : 0;
                    ordinalByType[childType] = ordinal;
                    CaptureNode(logicalChild, path + "/" + childType.FullName + "[" + ordinal + "]", states);
                }
            }

            private static void BuildControlLookup(
                global::Avalonia.LogicalTree.ILogical node,
                string path,
                List<LiveControlTarget> controls)
            {
                if (node is global::Avalonia.Controls.Control control)
                {
                    controls.Add(new LiveControlTarget(
                        control,
                        path,
                        TryGetNormalizedName(control),
                        BuildControlSemanticSignature(control)));
                }

                var ordinalByType = new Dictionary<Type, int>();
                foreach (var child in node.LogicalChildren)
                {
                    if (child is not global::Avalonia.LogicalTree.ILogical logicalChild)
                    {
                        continue;
                    }

                    var childType = logicalChild.GetType();
                    var ordinal = ordinalByType.TryGetValue(childType, out var currentOrdinal)
                        ? currentOrdinal + 1
                        : 0;
                    ordinalByType[childType] = ordinal;
                    BuildControlLookup(logicalChild, path + "/" + childType.FullName + "[" + ordinal + "]", controls);
                }
            }

            private static string? TryGetNormalizedName(global::Avalonia.Controls.Control control)
            {
                if (string.IsNullOrWhiteSpace(control.Name))
                {
                    return null;
                }

                return control.Name.Trim();
            }

            private static string BuildControlSemanticSignature(global::Avalonia.Controls.Control control)
            {
                var builder = new StringBuilder(128);
                builder.Append(control.GetType().FullName);
                AppendToken(builder, "class", TryGetStableClassSignature(control.Classes));
                AppendToken(builder, "width", double.IsNaN(control.Width) ? null : control.Width.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                AppendToken(builder, "height", double.IsNaN(control.Height) ? null : control.Height.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

                if (control.Margin != default)
                {
                    AppendToken(
                        builder,
                        "margin",
                        control.Margin.Left.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        control.Margin.Top.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        control.Margin.Right.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        control.Margin.Bottom.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }

                switch (control)
                {
                    case global::Avalonia.Controls.TextBox textBox:
                        AppendToken(builder, "watermark", textBox.Watermark?.ToString());
                        AppendToken(builder, "maxlength", textBox.MaxLength > 0 ? textBox.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                        break;
                    case global::Avalonia.Controls.Primitives.HeaderedContentControl headeredContentControl:
                        AppendToken(builder, "header", headeredContentControl.Header?.ToString());
                        break;
                    case global::Avalonia.Controls.Primitives.HeaderedSelectingItemsControl headeredSelectingItemsControl:
                        AppendToken(builder, "header", headeredSelectingItemsControl.Header?.ToString());
                        break;
                    case global::Avalonia.Controls.ContentControl contentControl:
                        if (contentControl.Content is string contentText)
                        {
                            AppendToken(builder, "content", contentText);
                        }
                        break;
                }

                return builder.ToString();
            }

            private static string? TryGetStableClassSignature(global::Avalonia.Controls.Classes classes)
            {
                if (classes.Count == 0)
                {
                    return null;
                }

                var stableClasses = new List<string>(classes.Count);
                for (var index = 0; index < classes.Count; index++)
                {
                    var candidate = classes[index];
                    if (string.IsNullOrWhiteSpace(candidate) ||
                        candidate[0] == ':')
                    {
                        continue;
                    }

                    stableClasses.Add(candidate.Trim());
                }

                if (stableClasses.Count == 0)
                {
                    return null;
                }

                stableClasses.Sort(StringComparer.Ordinal);
                return string.Join(",", stableClasses);
            }

            private static void AppendToken(StringBuilder builder, string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                builder.Append('|');
                builder.Append(key);
                builder.Append('=');
                builder.Append(value.Trim());
            }

            private readonly struct CapturedControlState(
                string path,
                string? name,
                string signature,
                Type controlType,
                TransientLocalValueStateSnapshot state)
            {
                public string Path { get; } = path;

                public string? Name { get; } = name;

                public string Signature { get; } = signature;

                public Type ControlType { get; } = controlType;

                public TransientLocalValueStateSnapshot State { get; } = state;
            }

            private readonly struct LiveControlTarget(
                global::Avalonia.Controls.Control control,
                string path,
                string? name,
                string signature)
            {
                public global::Avalonia.Controls.Control Control { get; } = control;

                public string Path { get; } = path;

                public string? Name { get; } = name;

                public string Signature { get; } = signature;
            }

        }
    }

    private readonly struct TransientLocalValueStateSnapshot
    {
        private readonly IReadOnlyList<CapturedLocalValue>? _values;

        private TransientLocalValueStateSnapshot(IReadOnlyList<CapturedLocalValue> values)
        {
            _values = values;
        }

        public bool IsEmpty => _values is null || _values.Count == 0;

        public static TransientLocalValueStateSnapshot Capture(global::Avalonia.AvaloniaObject target)
        {
            try
            {
                var valuesByProperty = new Dictionary<global::Avalonia.AvaloniaProperty, object?>();
                var targetType = target.GetType();
                foreach (var property in EnumerateCandidateProperties(targetType))
                {
                    if (!CanCaptureProperty(targetType, property))
                    {
                        continue;
                    }

                    global::Avalonia.Diagnostics.AvaloniaPropertyValue diagnostic;
                    try
                    {
                        diagnostic = global::Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(target, property);
                    }
                    catch
                    {
                        continue;
                    }

                    if (diagnostic.Priority != global::Avalonia.Data.BindingPriority.LocalValue)
                    {
                        continue;
                    }

                    var effectiveValue = diagnostic.Value;
                    if (!IsSnapshotSafeValue(effectiveValue))
                    {
                        continue;
                    }

                    valuesByProperty[property] = effectiveValue;
                }

                if (valuesByProperty.Count == 0)
                {
                    return default;
                }

                var capturedValues = new List<CapturedLocalValue>(valuesByProperty.Count);
                foreach (var pair in valuesByProperty)
                {
                    capturedValues.Add(new CapturedLocalValue(pair.Key, pair.Value));
                }

                capturedValues.Sort(static (left, right) => string.Compare(left.Property.Name, right.Property.Name, StringComparison.Ordinal));
                return new TransientLocalValueStateSnapshot(capturedValues);
            }
            catch
            {
                return default;
            }
        }

        public void Restore(global::Avalonia.AvaloniaObject target)
        {
            if (_values is null || _values.Count == 0)
            {
                return;
            }

            foreach (var capturedValue in _values)
            {
                try
                {
                    if (!CanRestoreProperty(capturedValue.Property, capturedValue.Value))
                    {
                        continue;
                    }

                    target.SetCurrentValue(capturedValue.Property, capturedValue.Value);
                }
                catch
                {
                    // Best effort restore only.
                }
            }
        }

        private static bool CanCaptureProperty(Type targetType, global::Avalonia.AvaloniaProperty property)
        {
            if (property.IsReadOnly)
            {
                return false;
            }

            if (ReferenceEquals(property, global::Avalonia.Controls.Control.ThemeProperty) ||
                ReferenceEquals(property, global::Avalonia.StyledElement.DataContextProperty))
            {
                return false;
            }

            if (ReferenceEquals(property, global::Avalonia.Controls.TextBox.CaretIndexProperty) ||
                ReferenceEquals(property, global::Avalonia.Controls.TextBox.SelectionStartProperty) ||
                ReferenceEquals(property, global::Avalonia.Controls.TextBox.SelectionEndProperty))
            {
                return true;
            }

            try
            {
                var metadata = property.GetMetadata(targetType);
                return metadata.DefaultBindingMode == global::Avalonia.Data.BindingMode.TwoWay;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<global::Avalonia.AvaloniaProperty> EnumerateCandidateProperties(Type targetType)
        {
            var yielded = new HashSet<global::Avalonia.AvaloniaProperty>();
            foreach (var property in global::Avalonia.AvaloniaPropertyRegistry.Instance.GetRegistered(targetType))
            {
                if (yielded.Add(property))
                {
                    yield return property;
                }
            }

            foreach (var property in global::Avalonia.AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(targetType))
            {
                if (yielded.Add(property))
                {
                    yield return property;
                }
            }
        }

        private static bool CanRestoreProperty(global::Avalonia.AvaloniaProperty property, object? value)
        {
            if (property.IsReadOnly)
            {
                return false;
            }

            if (value is null)
            {
                var valueType = property.PropertyType;
                return !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null;
            }

            return IsSnapshotSafeValue(value);
        }

        private static bool IsSnapshotSafeValue(object? value)
        {
            if (value is null)
            {
                return true;
            }

            if (value is global::Avalonia.UnsetValueType ||
                value is global::Avalonia.Visual ||
                value is global::Avalonia.LogicalTree.ILogical ||
                value is global::Avalonia.Styling.IStyle ||
                value is global::Avalonia.Controls.Templates.IDataTemplate ||
                value is global::Avalonia.Data.IBinding)
            {
                return false;
            }

            var valueType = value.GetType();
            if (valueType.IsPrimitive || valueType.IsEnum || valueType.IsValueType)
            {
                return true;
            }

            if (value is string ||
                value is Uri ||
                value is DateTime ||
                value is DateTimeOffset ||
                value is TimeSpan ||
                value is Guid ||
                value is Version)
            {
                return true;
            }

            if (value is System.Collections.IDictionary ||
                value is System.Collections.IList)
            {
                return false;
            }

            return true;
        }

        private readonly struct CapturedLocalValue(global::Avalonia.AvaloniaProperty property, object? value)
        {
            public global::Avalonia.AvaloniaProperty Property { get; } = property;

            public object? Value { get; } = value;
        }
    }

    private sealed class StyleHostVisualRefreshHotReloadHandler : ISourceGenHotReloadHandler
    {
        private IReadOnlyList<Type>? _requestedTypes;

        public int Priority => -210;

        public bool CanHandle(Type reloadType, object instance)
        {
            return instance is global::Avalonia.Styling.IStyle ||
                   instance is global::Avalonia.Controls.IResourceProvider;
        }

        public void BeforeVisualTreeUpdate(SourceGenHotReloadUpdateContext context)
        {
            _requestedTypes = context.RequestedTypes;
        }

        public void ReloadCompleted(SourceGenHotReloadUpdateContext context)
        {
            _requestedTypes = null;
        }

        public void AfterElementReload(Type reloadType, object instance, object? state)
        {
            TryNotifyHostedResourcesChanged(instance);
            TryNotifyApplicationHostedResourcesChanged();
            TryReinsertStyleIntoApplicationHost(instance);
            var affectedThemeTargetTypes = ResolveAffectedControlThemeTargetTypes(_requestedTypes);
            VisualTreeRematerializationUtilities.ScheduleThemeRefresh(affectedThemeTargetTypes);
        }

        private static IReadOnlyCollection<Type> ResolveAffectedControlThemeTargetTypes(IReadOnlyList<Type>? requestedTypes)
        {
            if (requestedTypes is null || requestedTypes.Count == 0)
            {
                return Array.Empty<Type>();
            }

            var affectedUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < requestedTypes.Count; index++)
            {
                var requestedType = requestedTypes[index];
                if (requestedType is null)
                {
                    continue;
                }

                if (XamlSourceGenTypeUriRegistry.TryGetUri(requestedType, out var uri) &&
                    !string.IsNullOrWhiteSpace(uri))
                {
                    affectedUris.Add(uri);
                }
            }

            if (affectedUris.Count == 0)
            {
                return Array.Empty<Type>();
            }

            var targets = new HashSet<Type>();
            foreach (var uri in affectedUris)
            {
                var descriptors = XamlControlThemeRegistry.GetAll(uri);
                foreach (var descriptor in descriptors)
                {
                    if (string.IsNullOrWhiteSpace(descriptor.TargetTypeName))
                    {
                        continue;
                    }

                    var normalizedTypeName = descriptor.TargetTypeName.Trim();
                    if (normalizedTypeName.StartsWith("global::", StringComparison.Ordinal))
                    {
                        normalizedTypeName = normalizedTypeName["global::".Length..];
                    }

                    if (SourceGenKnownTypeRegistry.TryResolve(xmlNamespace: null, normalizedTypeName, out var targetType) &&
                        targetType is not null)
                    {
                        targets.Add(targetType);
                    }
                }
            }

            return targets.Count == 0
                ? Array.Empty<Type>()
                : targets.ToArray();
        }

        private static void TryNotifyHostedResourcesChanged(object instance)
        {
            try
            {
                switch (instance)
                {
                    case global::Avalonia.Styling.StyleBase styleBase when styleBase.Owner is global::Avalonia.Controls.IResourceHost styleOwner:
                        styleOwner.NotifyHostedResourcesChanged(global::Avalonia.Controls.ResourcesChangedEventArgs.Empty);
                        Trace("Notified hosted resource change for style owner '" + styleOwner.GetType().FullName + "'.");
                        break;
                    case global::Avalonia.Styling.Styles styles when styles.Owner is global::Avalonia.Controls.IResourceHost stylesOwner:
                        stylesOwner.NotifyHostedResourcesChanged(global::Avalonia.Controls.ResourcesChangedEventArgs.Empty);
                        Trace("Notified hosted resource change for styles owner '" + stylesOwner.GetType().FullName + "'.");
                        break;
                }
            }
            catch
            {
                // Best effort hosted-resource invalidation only.
            }
        }

        private static void TryNotifyApplicationHostedResourcesChanged()
        {
            try
            {
                if (global::Avalonia.Application.Current is global::Avalonia.Controls.IResourceHost resourceHost)
                {
                    resourceHost.NotifyHostedResourcesChanged(global::Avalonia.Controls.ResourcesChangedEventArgs.Empty);
                    Trace("Notified hosted resource change for application resource host.");
                }
            }
            catch
            {
                // Best effort application resource invalidation only.
            }
        }

        private static void TryReinsertStyleIntoApplicationHost(object instance)
        {
            if (instance is not global::Avalonia.Styling.IStyle style)
            {
                return;
            }

            try
            {
                if (global::Avalonia.Application.Current is not global::Avalonia.Styling.IStyleHost styleHost ||
                    !styleHost.IsStylesInitialized)
                {
                    return;
                }

                var styles = styleHost.Styles;
                for (var index = 0; index < styles.Count; index++)
                {
                    if (!ReferenceEquals(styles[index], style))
                    {
                        continue;
                    }

                    styles.RemoveAt(index);
                    styles.Insert(index, style);
                    Trace("Reinserted style instance into application style host at index " + index + ".");
                    return;
                }
            }
            catch
            {
                // Best effort style-host reinsert only.
            }
        }

    }

    private static class VisualTreeRematerializationUtilities
    {
        private sealed class ThemeOverrideMarker
        {
        }

        private static readonly object ThemeRefreshSync = new();
        private static readonly HashSet<Type> PendingAffectedControlThemeTargetTypes = new();
        private static readonly ConditionalWeakTable<global::Avalonia.Controls.Control, ThemeOverrideMarker> ManagedThemeOverrides = new();
        private static int ThemeRefreshScheduled;

        public static void RefreshAffectedImplicitControlThemes(IReadOnlyCollection<Type> affectedTargetTypes)
        {
            if (affectedTargetTypes.Count == 0)
            {
                return;
            }

            var roots = CaptureApplicationRootVisuals();
            for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                var visuals = CaptureVisualSnapshot(roots[rootIndex]);
                for (var visualIndex = visuals.Count - 1; visualIndex >= 0; visualIndex--)
                {
                    if (visuals[visualIndex] is global::Avalonia.Controls.Control control)
                    {
                        TryRefreshImplicitControlTheme(control, affectedTargetTypes);
                    }
                }
            }
        }

        public static void RefreshApplicationVisualTreeAfterThemeUpdate()
        {
            var roots = CaptureApplicationRootVisuals();
            for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                var root = roots[rootIndex];
                TryInvalidateRootVisual(root);
            }
        }

        public static void ScheduleThemeRefresh(IReadOnlyCollection<Type> affectedTargetTypes)
        {
            lock (ThemeRefreshSync)
            {
                if (affectedTargetTypes.Count > 0)
                {
                    foreach (var targetType in affectedTargetTypes)
                    {
                        PendingAffectedControlThemeTargetTypes.Add(targetType);
                    }
                }
            }

            if (Interlocked.Exchange(ref ThemeRefreshScheduled, 1) == 1)
            {
                return;
            }

            try
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(
                    ApplyScheduledThemeRefresh,
                    global::Avalonia.Threading.DispatcherPriority.Background);
            }
            catch
            {
                ApplyScheduledThemeRefresh();
            }
        }

        private static void ApplyScheduledThemeRefresh()
        {
            Type[] affectedTargetTypes;
            lock (ThemeRefreshSync)
            {
                affectedTargetTypes = PendingAffectedControlThemeTargetTypes.ToArray();
                PendingAffectedControlThemeTargetTypes.Clear();
                Interlocked.Exchange(ref ThemeRefreshScheduled, 0);
            }

            if (affectedTargetTypes.Length > 0)
            {
                try
                {
                    RefreshAffectedImplicitControlThemes(affectedTargetTypes);
                }
                catch
                {
                    // Best effort control-theme refresh only.
                }
            }

            try
            {
                RefreshApplicationVisualTreeAfterThemeUpdate();
            }
            catch
            {
                // Best effort root invalidation only.
            }
        }

        private static IReadOnlyList<global::Avalonia.Visual> CaptureApplicationRootVisuals()
        {
            var roots = new List<global::Avalonia.Visual>(4);

            var application = global::Avalonia.Application.Current;
            if (application is null)
            {
                return roots;
            }

            if (application.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                if (desktopLifetime.MainWindow is global::Avalonia.Visual mainWindowVisual)
                {
                    roots.Add(mainWindowVisual);
                }

                try
                {
                    var windows = desktopLifetime.Windows;
                    for (var index = 0; index < windows.Count; index++)
                    {
                        if (windows[index] is not global::Avalonia.Visual windowVisual ||
                            ContainsVisualReference(roots, windowVisual))
                        {
                            continue;
                        }

                        roots.Add(windowVisual);
                    }
                }
                catch
                {
                    // Best effort window enumeration only.
                }
            }

            if (application.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime singleViewLifetime &&
                singleViewLifetime.MainView is global::Avalonia.Visual singleViewVisual &&
                !ContainsVisualReference(roots, singleViewVisual))
            {
                roots.Add(singleViewVisual);
            }

            return roots;
        }

        private static bool ContainsVisualReference(List<global::Avalonia.Visual> roots, global::Avalonia.Visual candidate)
        {
            for (var index = 0; index < roots.Count; index++)
            {
                if (ReferenceEquals(roots[index], candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryRefreshImplicitControlTheme(
            global::Avalonia.Controls.Control control,
            IReadOnlyCollection<Type> affectedTargetTypes)
        {
            try
            {
                if (control.TemplatedParent is not null)
                {
                    return;
                }

                if (control is global::Avalonia.LogicalTree.ILogical logical &&
                    !logical.IsAttachedToLogicalTree)
                {
                    return;
                }

                var styleKeyType = control.StyleKey;
                var hasManagedOverride = ManagedThemeOverrides.TryGetValue(control, out _);
                var affectsControl = IsAffectedControl(styleKeyType, affectedTargetTypes);

                if (!affectsControl && !hasManagedOverride)
                {
                    return;
                }

                // Preserve user-authored explicit theme overrides.
                if (control.Theme is not null && !hasManagedOverride)
                {
                    return;
                }

                if (!global::Avalonia.Controls.ResourceNodeExtensions.TryFindResource(
                        control,
                        styleKeyType,
                        out var resource) ||
                    resource is not global::Avalonia.Styling.ControlTheme resolvedTheme)
                {
                    if (hasManagedOverride)
                    {
                        control.Theme = null;
                        ManagedThemeOverrides.Remove(control);
                        Trace("Cleared managed control theme override for '" + control.GetType().FullName + "'.");
                    }

                    return;
                }

                if (ReferenceEquals(control.Theme, resolvedTheme))
                {
                    // Avoid destructive same-reference reapply for duplicate update signals.
                    // Resource invalidation and root layout invalidation are handled separately.
                    if (affectsControl)
                    {
                        Trace("Skipped same-reference implicit control theme reapply for '" + control.GetType().FullName + "'.");
                    }

                    return;
                }

                if (!TrySetControlTheme(control, resolvedTheme))
                {
                    Trace("Skipped implicit control theme refresh for '" + control.GetType().FullName + "' due unsafe content state.");
                    return;
                }

                ManagedThemeOverrides.Remove(control);
                ManagedThemeOverrides.Add(control, new ThemeOverrideMarker());
                Trace("Refreshed implicit control theme for '" + control.GetType().FullName + "'.");
            }
            catch (Exception ex)
            {
                Trace("Implicit control theme refresh failed for '" + control.GetType().FullName + "': " + ex.Message);
            }
        }

        private static bool IsAffectedControl(Type styleKeyType, IReadOnlyCollection<Type> affectedTargetTypes)
        {
            foreach (var targetType in affectedTargetTypes)
            {
                if (targetType == styleKeyType ||
                    targetType.IsAssignableFrom(styleKeyType) ||
                    styleKeyType.IsAssignableFrom(targetType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySetControlTheme(
            global::Avalonia.Controls.Control control,
            global::Avalonia.Styling.ControlTheme resolvedTheme)
        {
            return TryApplyControlTheme(control, resolvedTheme, forceReapply: false);
        }

        private static bool TryReapplyControlTheme(
            global::Avalonia.Controls.Control control,
            global::Avalonia.Styling.ControlTheme resolvedTheme)
        {
            return TryApplyControlTheme(control, resolvedTheme, forceReapply: true);
        }

        private static bool TryApplyControlTheme(
            global::Avalonia.Controls.Control control,
            global::Avalonia.Styling.ControlTheme resolvedTheme,
            bool forceReapply)
        {
            if (control is not global::Avalonia.Controls.ContentControl contentControl ||
                contentControl.Content is not global::Avalonia.Visual)
            {
                if (forceReapply)
                {
                    control.Theme = null;
                }

                control.Theme = resolvedTheme;
                return true;
            }

            var preservedContent = contentControl.Content;
            try
            {
                contentControl.Content = null;
                if (forceReapply)
                {
                    control.Theme = null;
                }

                control.Theme = resolvedTheme;
                contentControl.Content = preservedContent;
                return true;
            }
            catch
            {
                if (!ReferenceEquals(contentControl.Content, preservedContent))
                {
                    try
                    {
                        contentControl.Content = preservedContent;
                    }
                    catch
                    {
                        // Best effort content restore only.
                    }
                }

                return false;
            }
        }

        private static IReadOnlyList<global::Avalonia.Visual> CaptureVisualSnapshot(global::Avalonia.Visual rootVisual)
        {
            var visuals = new List<global::Avalonia.Visual>(64) { rootVisual };
            try
            {
                foreach (var visual in rootVisual.GetVisualDescendants())
                {
                    visuals.Add(visual);
                }
            }
            catch
            {
                // Best effort visual enumeration only.
            }

            return visuals;
        }

        private static void TryInvalidateRootVisual(global::Avalonia.Visual rootVisual)
        {
            try
            {
                if (rootVisual is global::Avalonia.Layout.Layoutable layoutable)
                {
                    layoutable.InvalidateMeasure();
                    layoutable.InvalidateArrange();
                }

                rootVisual.InvalidateVisual();
            }
            catch
            {
                // Best effort root invalidation only.
            }
        }
    }

    private static bool IsRudeEditException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is MissingMethodException ||
                current is MissingMemberException ||
                current is TypeLoadException ||
                current is InvalidProgramException ||
                current is BadImageFormatException ||
                current is MemberAccessException)
            {
                return true;
            }

            var message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            if (message.Contains("rude edit", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("ENC", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Edit and Continue", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Trace(string message)
    {
        if (!IsTraceEnabled())
        {
            return;
        }

        try
        {
            Debug.WriteLine("[AXSG.HotReload] " + message);
            Console.WriteLine("[AXSG.HotReload] " + message);
        }
        catch
        {
            // Best-effort tracing only.
        }
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

    private static string FormatTypeList(Type[]? types)
    {
        if (types is null || types.Length == 0)
        {
            return "<all>";
        }

        var values = new string[types.Length];
        for (var index = 0; index < types.Length; index++)
        {
            values[index] = types[index]?.FullName ?? "<null>";
        }

        return string.Join(", ", values);
    }

    private static string FormatOperationTargets(List<ReloadOperation> operations)
    {
        if (operations.Count == 0)
        {
            return "<none>";
        }

        var values = new string[operations.Count];
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            var instanceType = operation.Instance?.GetType().FullName ?? "<null>";
            values[index] = operation.Type.FullName + " <= " + instanceType;
        }

        return string.Join("; ", values);
    }
}
