using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
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
    private static readonly Dictionary<Type, Type> ReplacementTypeMap = new();
    private static readonly List<RegisteredHandler> Handlers = new();
    private static readonly HashSet<string> HandlerKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<Type> PendingReloadTypes = new();

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
                var operations = CollectReloadOperations(currentTypes);
                var context = BuildUpdateContext(currentTrigger, currentTypes, operations);
                Trace("UpdateApplication invoked. Trigger: " + currentTrigger + ". Candidate operations: " + operations.Count + ".");

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

            foreach (var requestedType in types)
            {
                var normalizedType = NormalizeType(requestedType);
                if (!TryResolveTrackedTypeLocked(normalizedType, out var trackedType))
                {
                    continue;
                }

                if (!Instances.TryGetValue(trackedType, out var references))
                {
                    continue;
                }

                if (!Registrations.TryGetValue(trackedType, out var registration))
                {
                    continue;
                }

                AddOperationsForType(trackedType, references, registration, operations);
            }

            return operations;
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
            if (operation.Instance is global::Avalonia.AvaloniaObject)
            {
                return true;
            }
        }

        return false;
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"XAML source generator hot reload failed for '{operation.Type.FullName}': {ex}");
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
}
