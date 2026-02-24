using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: MetadataUpdateHandler(typeof(XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotReloadManager
{
    private const int SourcePathReloadRetryCount = 5;
    private const string TraceEnvVarName = "AXSG_HOTRELOAD_TRACE";

    private static readonly object Sync = new();
    private static readonly Dictionary<Type, List<WeakReference<object>>> Instances = new();
    private static readonly Dictionary<Type, ReloadRegistration> Registrations = new();
    private static readonly Dictionary<Type, SourcePathWatchState> IdeSourcePathWatchers = new();
    private static readonly Dictionary<Type, string> BuildUrisByType = new();
    private static readonly Dictionary<Type, Type> ReplacementTypeMap = new();
    private static readonly List<RegisteredHandler> Handlers = new();
    private static readonly HashSet<string> HandlerKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<Type> PendingReloadTypes = new();
    private static readonly IXamlSourceGenUriMapper UriMapper = XamlSourceGenUriMapper.Default;

    private static Timer? IdePollingTimer;
    private static int IdePollingIntervalMs = 1000;
    private static bool? TraceEnabledCached;
    private static bool ReloadInProgress;
    private static bool PendingReloadAllTypes;

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

    public static bool IsEnabled { get; private set; } = true;

    public static bool IsIdePollingFallbackEnabled { get; private set; }

    public static void Enable()
    {
        IsEnabled = true;
    }

    public static void Disable()
    {
        IsEnabled = false;
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
        var modifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        return string.Equals(modifiableAssemblies, "debug", StringComparison.OrdinalIgnoreCase);
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

    private static void UpdateApplicationCore(Type[]? types, SourceGenHotReloadTrigger trigger)
    {
        var eventBus = XamlSourceGenHotReloadEventBus.Instance;
        var normalizedTypes = NormalizeUpdatedTypes(types);
        if (!IsEnabled)
        {
            HotReloaded?.Invoke(normalizedTypes);
            eventBus.PublishHotReloaded(normalizedTypes);
            return;
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
            while (true)
            {
                var refreshedTypes = RefreshArtifactsForUpdatedTypes(currentTypes);
                var operations = CollectReloadOperations(currentTypes);
                var context = BuildUpdateContext(currentTrigger, currentTypes, operations);
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

                lock (Sync)
                {
                    if (!TryDequeuePendingReloadLocked(out currentTypes))
                    {
                        break;
                    }

                    currentTrigger = SourceGenHotReloadTrigger.Queued;
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

            if (TryGetBuildUriForTypeLocked(normalizedType, out var buildUri))
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
        List<ReloadOperation> operations)
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
            operations.Count);
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
            InvokeBeforeVisualTreeUpdate(context, handlers);
            foreach (var operation in operations)
            {
                ExecuteReload(operation, handlers);
            }

            InvokeAfterVisualTreeUpdate(context, handlers);
            InvokeReloadCompleted(context, handlers);
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
        IReadOnlyList<RegisteredHandler> handlers)
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
            operation.Registration.ReloadAction(operation.Instance);
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
        AddHandlerLocked(new StyledElementDataContextHotReloadHandler(), typeof(global::Avalonia.StyledElement), "default");
        AddHandlerLocked(new VisualTemplateRematerializationHotReloadHandler(), typeof(global::Avalonia.Visual), "default");
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

    private sealed class VisualTemplateRematerializationHotReloadHandler : ISourceGenHotReloadHandler
    {
        public int Priority => -200;

        public bool CanHandle(Type reloadType, object instance)
        {
            return instance is global::Avalonia.Visual;
        }

        public void AfterElementReload(Type reloadType, object instance, object? state)
        {
            if (instance is not global::Avalonia.Visual rootVisual)
            {
                return;
            }

            VisualTreeRematerializationUtilities.RematerializeFromRootVisual(rootVisual);
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
            TryReattachApplicationStyles();
            var affectedThemeTargetTypes = ResolveAffectedControlThemeTargetTypes(_requestedTypes);
            VisualTreeRematerializationUtilities.RefreshAffectedImplicitControlThemes(affectedThemeTargetTypes);
            VisualTreeRematerializationUtilities.RefreshApplicationVisualTreeAfterThemeUpdate();
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

        private static void TryReattachApplicationStyles()
        {
            try
            {
                if (global::Avalonia.Application.Current is not global::Avalonia.Styling.IStyleHost styleHost ||
                    !styleHost.IsStylesInitialized)
                {
                    return;
                }

                var styles = styleHost.Styles;
                if (styles.Count == 0)
                {
                    return;
                }

                var snapshot = new List<global::Avalonia.Styling.IStyle>(styles.Count);
                for (var index = 0; index < styles.Count; index++)
                {
                    snapshot.Add(styles[index]);
                }

                styles.Clear();
                for (var index = 0; index < snapshot.Count; index++)
                {
                    styles.Add(snapshot[index]);
                }

                Trace("Reattached application styles collection after style/resource reload. Count: " + snapshot.Count + ".");
            }
            catch
            {
                // Best effort style-host reattachment only.
            }
        }
    }

    private static class VisualTreeRematerializationUtilities
    {
        private sealed class ThemeOverrideMarker
        {
        }

        private static readonly ConditionalWeakTable<global::Avalonia.Controls.Control, ThemeOverrideMarker> ManagedThemeOverrides = new();

        public static void RematerializeApplicationVisualRoots()
        {
            var roots = CaptureApplicationRootVisuals();
            for (var index = 0; index < roots.Count; index++)
            {
                RematerializeFromRootVisual(roots[index]);
            }
        }

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
                for (var visualIndex = 0; visualIndex < visuals.Count; visualIndex++)
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

        public static void RematerializeFromRootVisual(global::Avalonia.Visual rootVisual)
        {
            // Two passes ensure template-created descendants are also materialized.
            for (var pass = 0; pass < 2; pass++)
            {
                var visuals = CaptureVisualSnapshot(rootVisual);
                for (var index = 0; index < visuals.Count; index++)
                {
                    if (visuals[index] is global::Avalonia.StyledElement styledElement)
                    {
                        TryApplyStyling(styledElement);
                    }
                }

                for (var index = 0; index < visuals.Count; index++)
                {
                    if (visuals[index] is global::Avalonia.Layout.Layoutable layoutable)
                    {
                        TryApplyTemplate(layoutable);
                    }
                }
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
                    return;
                }

                control.Theme = resolvedTheme;
                ManagedThemeOverrides.Remove(control);
                ManagedThemeOverrides.Add(control, new ThemeOverrideMarker());
                Trace("Refreshed control theme for '" + control.GetType().FullName + "' using target '" + styleKeyType.FullName + "'.");
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

        private static void TryApplyStyling(global::Avalonia.StyledElement styledElement)
        {
            try
            {
                styledElement.ApplyStyling();
            }
            catch
            {
                // Best effort style apply only.
            }
        }

        private static void TryApplyTemplate(global::Avalonia.Layout.Layoutable layoutable)
        {
            try
            {
                layoutable.ApplyTemplate();
            }
            catch
            {
                // Best effort template materialization only.
            }
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
