using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotDesignManager
{
    private const string TraceEnvVarName = "AXSG_HOTDESIGN_TRACE";
    private static readonly object Sync = new();
    private static readonly Dictionary<Type, Registration> Registrations = new();
    private static readonly Dictionary<string, Type> TypeByBuildUri = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ISourceGenHotDesignUpdateApplier> Appliers = new();
    private static readonly HashSet<string> ApplierKeys = new(StringComparer.Ordinal);

    private static SourceGenHotDesignOptions ActiveOptions = new();
    private static bool? TraceEnabledCached;

    static XamlSourceGenHotDesignManager()
    {
        lock (Sync)
        {
            AddApplierLocked(new FileSystemHotDesignUpdateApplier());
        }
    }

    public static bool IsEnabled { get; private set; }

    public static event Action<bool>? HotDesignModeChanged;

    public static event Action<SourceGenHotDesignApplyResult>? HotDesignUpdateApplied;

    public static event Action<SourceGenHotDesignUpdateRequest, Exception>? HotDesignUpdateFailed;

    public static void Enable(SourceGenHotDesignOptions? options = null)
    {
        lock (Sync)
        {
            IsEnabled = true;
            if (options is not null)
            {
                ActiveOptions = options.Clone();
            }
        }

        HotDesignModeChanged?.Invoke(true);
    }

    public static void Disable()
    {
        lock (Sync)
        {
            IsEnabled = false;
        }

        HotDesignModeChanged?.Invoke(false);
    }

    public static bool Toggle()
    {
        bool enabled;
        lock (Sync)
        {
            IsEnabled = !IsEnabled;
            enabled = IsEnabled;
        }

        HotDesignModeChanged?.Invoke(enabled);
        return enabled;
    }

    public static void Configure(Action<SourceGenHotDesignOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        lock (Sync)
        {
            var clone = ActiveOptions.Clone();
            configure(clone);
            ActiveOptions = clone;
        }
    }

    public static SourceGenHotDesignStatus GetStatus()
    {
        lock (Sync)
        {
            return new SourceGenHotDesignStatus(
                IsEnabled,
                Registrations.Count,
                Appliers.Count,
                ActiveOptions.Clone());
        }
    }

    public static void Register(
        object instance,
        Action<object> runtimeApplyAction,
        SourceGenHotDesignRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(runtimeApplyAction);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.BuildUri))
        {
            throw new ArgumentException("BuildUri must be provided.", nameof(options));
        }

        var trackedType = NormalizeType(instance.GetType());
        var normalizedBuildUri = options.BuildUri.Trim();
        var normalizedSourcePath = NormalizeSourcePath(options.SourcePath);

        lock (Sync)
        {
            if (!Registrations.TryGetValue(trackedType, out var registration))
            {
                registration = new Registration(
                    trackedType,
                    normalizedBuildUri,
                    normalizedSourcePath,
                    runtimeApplyAction);
                Registrations[trackedType] = registration;
            }
            else
            {
                registration.BuildUri = normalizedBuildUri;
                registration.SourcePath = normalizedSourcePath;
                registration.RuntimeApplyAction = runtimeApplyAction;
            }

            TypeByBuildUri[normalizedBuildUri] = trackedType;
            PruneDeadReferences(registration.Instances);
            if (!ContainsReference(registration.Instances, instance))
            {
                registration.Instances.Add(new WeakReference<object>(instance));
            }
        }
    }

    public static void ClearRegistrations()
    {
        lock (Sync)
        {
            Registrations.Clear();
            TypeByBuildUri.Clear();
        }

        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    public static void RegisterApplier(ISourceGenHotDesignUpdateApplier applier)
    {
        ArgumentNullException.ThrowIfNull(applier);

        lock (Sync)
        {
            AddApplierLocked(applier);
        }
    }

    public static void ResetAppliersToDefaults()
    {
        lock (Sync)
        {
            Appliers.Clear();
            ApplierKeys.Clear();
            AddApplierLocked(new FileSystemHotDesignUpdateApplier());
        }
    }

    public static IReadOnlyList<SourceGenHotDesignDocumentDescriptor> GetRegisteredDocuments()
    {
        lock (Sync)
        {
            var results = new List<SourceGenHotDesignDocumentDescriptor>(Registrations.Count);
            foreach (var registration in Registrations.Values)
            {
                PruneDeadReferences(registration.Instances);
                results.Add(new SourceGenHotDesignDocumentDescriptor(
                    registration.RootType,
                    registration.BuildUri,
                    registration.SourcePath,
                    registration.Instances.Count));
            }

            return results
                .OrderBy(static descriptor => descriptor.BuildUri, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static SourceGenHotDesignApplyResult ApplyUpdate(
        SourceGenHotDesignUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ApplyUpdateAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    public static async ValueTask<SourceGenHotDesignApplyResult> ApplyUpdateAsync(
        SourceGenHotDesignUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.XamlText))
        {
            var emptyResult = new SourceGenHotDesignApplyResult(
                Succeeded: false,
                Message: "Hot design update request has empty XAML text.");
            HotDesignUpdateApplied?.Invoke(emptyResult);
            return emptyResult;
        }

        Registration? registration;
        SourceGenHotDesignOptions options;
        ISourceGenHotDesignUpdateApplier[] appliers;
        SourceGenHotDesignDocumentDescriptor descriptor;
        IReadOnlyList<object> trackedInstances;

        lock (Sync)
        {
            if (!IsEnabled)
            {
                var disabledResult = new SourceGenHotDesignApplyResult(
                    Succeeded: false,
                    Message: "Hot design mode is disabled.");
                HotDesignUpdateApplied?.Invoke(disabledResult);
                return disabledResult;
            }

            if (!TryResolveRegistrationLocked(request, out registration))
            {
                var missingResult = new SourceGenHotDesignApplyResult(
                    Succeeded: false,
                    Message: "No registered source-generated XAML document matched the update request.",
                    BuildUri: request.BuildUri,
                    TargetType: request.TargetType);
                HotDesignUpdateApplied?.Invoke(missingResult);
                return missingResult;
            }

            var resolvedRegistration = registration!;
            PruneDeadReferences(resolvedRegistration.Instances);

            trackedInstances = SnapshotInstances(resolvedRegistration.Instances);
            descriptor = new SourceGenHotDesignDocumentDescriptor(
                resolvedRegistration.RootType,
                resolvedRegistration.BuildUri,
                resolvedRegistration.SourcePath,
                trackedInstances.Count);

            options = ActiveOptions.Clone();
            if (request.PersistChangesToSource.HasValue)
            {
                options.PersistChangesToSource = request.PersistChangesToSource.Value;
            }

            if (request.WaitForHotReload.HasValue)
            {
                options.WaitForHotReload = request.WaitForHotReload.Value;
            }

            if (request.FallbackToRuntimeApplyOnTimeout.HasValue)
            {
                options.FallbackToRuntimeApplyOnTimeout = request.FallbackToRuntimeApplyOnTimeout.Value;
            }

            appliers = Appliers.ToArray();
            registration = resolvedRegistration;
        }

        if (registration is null)
        {
            var unresolvedResult = new SourceGenHotDesignApplyResult(
                Succeeded: false,
                Message: "No registered source-generated XAML document matched the update request.",
                BuildUri: request.BuildUri,
                TargetType: request.TargetType);
            HotDesignUpdateApplied?.Invoke(unresolvedResult);
            return unresolvedResult;
        }

        var context = new SourceGenHotDesignUpdateContext(
            descriptor,
            request,
            options,
            trackedInstances,
            registration.RuntimeApplyAction);

        foreach (var applier in appliers)
        {
            if (!applier.CanApply(context))
            {
                continue;
            }

            try
            {
                var result = await applier.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
                HotDesignUpdateApplied?.Invoke(result);
                Trace(
                    "Applied hot design update for '" + descriptor.BuildUri + "'. Success=" + result.Succeeded + ".",
                    options.EnableTracing);
                return result;
            }
            catch (Exception ex)
            {
                HotDesignUpdateFailed?.Invoke(request, ex);
                var failedResult = new SourceGenHotDesignApplyResult(
                    Succeeded: false,
                    Message: "Hot design update applier '" + applier.GetType().FullName + "' failed: " + ex.Message,
                    BuildUri: descriptor.BuildUri,
                    TargetType: descriptor.RootType,
                    SourcePath: descriptor.SourcePath,
                    Error: ex);
                HotDesignUpdateApplied?.Invoke(failedResult);
                return failedResult;
            }
        }

        var noApplierResult = new SourceGenHotDesignApplyResult(
            Succeeded: false,
            Message: "No registered hot design update applier could handle the request.",
            BuildUri: descriptor.BuildUri,
            TargetType: descriptor.RootType,
            SourcePath: descriptor.SourcePath);
        HotDesignUpdateApplied?.Invoke(noApplierResult);
        return noApplierResult;
    }

    private static bool TryResolveRegistrationLocked(
        SourceGenHotDesignUpdateRequest request,
        out Registration? registration)
    {
        registration = null;

        if (request.TargetType is not null)
        {
            var normalizedType = NormalizeType(request.TargetType);
            if (Registrations.TryGetValue(normalizedType, out registration))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TargetTypeName))
        {
            var targetTypeName = request.TargetTypeName.Trim();
            foreach (var pair in Registrations)
            {
                var type = pair.Key;
                if (string.Equals(type.FullName, targetTypeName, StringComparison.Ordinal) ||
                    string.Equals(type.Name, targetTypeName, StringComparison.Ordinal))
                {
                    registration = pair.Value;
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.BuildUri) &&
            TypeByBuildUri.TryGetValue(request.BuildUri.Trim(), out var trackedType) &&
            Registrations.TryGetValue(trackedType, out registration))
        {
            return true;
        }

        return false;
    }

    private static Type NormalizeType(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }

    private static string? NormalizeSourcePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(sourcePath.Trim());
        }
        catch
        {
            return sourcePath.Trim();
        }
    }

    private static void AddApplierLocked(ISourceGenHotDesignUpdateApplier applier)
    {
        var applierKey = applier.GetType().AssemblyQualifiedName ?? applier.GetType().FullName ?? applier.GetType().Name;
        if (!ApplierKeys.Add(applierKey))
        {
            return;
        }

        Appliers.Add(applier);
        Appliers.Sort(static (left, right) =>
        {
            var compare = right.Priority.CompareTo(left.Priority);
            if (compare != 0)
            {
                return compare;
            }

            var leftName = left.GetType().FullName ?? string.Empty;
            var rightName = right.GetType().FullName ?? string.Empty;
            return string.Compare(leftName, rightName, StringComparison.Ordinal);
        });
    }

    private static IReadOnlyList<object> SnapshotInstances(List<WeakReference<object>> references)
    {
        if (references.Count == 0)
        {
            return Array.Empty<object>();
        }

        var instances = new List<object>(references.Count);
        foreach (var reference in references)
        {
            if (reference.TryGetTarget(out var instance) &&
                instance is not null)
            {
                instances.Add(instance);
            }
        }

        return instances;
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

    private static async ValueTask<bool> WaitForHotReloadAsync(
        Type rootType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        var normalizedRootType = NormalizeType(rootType);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(SourceGenHotReloadUpdateContext context)
        {
            for (var index = 0; index < context.ReloadedTypes.Count; index++)
            {
                if (NormalizeType(context.ReloadedTypes[index]) == normalizedRootType)
                {
                    completion.TrySetResult(true);
                    return;
                }
            }
        }

        XamlSourceGenHotReloadEventBus.Instance.HotReloadPipelineCompleted += OnCompleted;
        try
        {
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedTokenSource.CancelAfter(timeout);
            var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, linkedTokenSource.Token);
            var completedTask = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);
            return completedTask == completion.Task && completion.Task.IsCompletedSuccessfully;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            XamlSourceGenHotReloadEventBus.Instance.HotReloadPipelineCompleted -= OnCompleted;
        }
    }

    private static void Trace(string message, bool forceTrace)
    {
        if (!forceTrace && !IsTraceEnabled())
        {
            return;
        }

        try
        {
            Debug.WriteLine("[AXSG.HotDesign] " + message);
            Console.WriteLine("[AXSG.HotDesign] " + message);
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

    private sealed class FileSystemHotDesignUpdateApplier : ISourceGenHotDesignUpdateApplier
    {
        public int Priority => 0;

        public bool CanApply(SourceGenHotDesignUpdateContext context)
        {
            return true;
        }

        public async ValueTask<SourceGenHotDesignApplyResult> ApplyAsync(
            SourceGenHotDesignUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            var options = context.Options;
            var document = context.Document;
            var request = context.Request;
            var persistChangesToSource = options.PersistChangesToSource;
            var waitForHotReload = options.WaitForHotReload;
            var fallbackToRuntimeApply = options.FallbackToRuntimeApplyOnTimeout;

            var sourcePersisted = false;
            var hotReloadObserved = false;
            var runtimeFallbackApplied = false;

            if (persistChangesToSource)
            {
                if (string.IsNullOrWhiteSpace(document.SourcePath))
                {
                    return new SourceGenHotDesignApplyResult(
                        Succeeded: false,
                        Message: "Hot design update requested source persistence, but no source path is registered for the target document.",
                        BuildUri: document.BuildUri,
                        TargetType: document.RootType,
                        SourcePath: document.SourcePath);
                }

                try
                {
                    var sourcePath = document.SourcePath!;
                    var directory = Path.GetDirectoryName(sourcePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(sourcePath, request.XamlText);
                    sourcePersisted = true;
                }
                catch (Exception ex)
                {
                    return new SourceGenHotDesignApplyResult(
                        Succeeded: false,
                        Message: "Failed to persist hot design source update: " + ex.Message,
                        BuildUri: document.BuildUri,
                        TargetType: document.RootType,
                        SourcePath: document.SourcePath,
                        Error: ex);
                }
            }

            if (persistChangesToSource && waitForHotReload)
            {
                hotReloadObserved = await WaitForHotReloadAsync(
                    document.RootType,
                    options.HotReloadWaitTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!persistChangesToSource || (!hotReloadObserved && fallbackToRuntimeApply))
            {
                var fallbackAppliedCount = context.ApplyRuntimeToTrackedInstances(static _ => { });
                runtimeFallbackApplied = fallbackAppliedCount > 0;
            }

            if (!persistChangesToSource)
            {
                if (runtimeFallbackApplied)
                {
                    return new SourceGenHotDesignApplyResult(
                        Succeeded: true,
                        Message: "Applied hot design update directly to tracked runtime instances.",
                        BuildUri: document.BuildUri,
                        TargetType: document.RootType,
                        SourcePath: document.SourcePath,
                        SourcePersisted: false,
                        HotReloadObserved: false,
                        RuntimeFallbackApplied: true);
                }

                return new SourceGenHotDesignApplyResult(
                    Succeeded: false,
                    Message: "No tracked runtime instances were available for direct hot design apply.",
                    BuildUri: document.BuildUri,
                    TargetType: document.RootType,
                    SourcePath: document.SourcePath,
                    SourcePersisted: false,
                    HotReloadObserved: false,
                    RuntimeFallbackApplied: false);
            }

            if (waitForHotReload)
            {
                if (hotReloadObserved)
                {
                    return new SourceGenHotDesignApplyResult(
                        Succeeded: true,
                        Message: "Persisted hot design source update and observed hot reload completion.",
                        BuildUri: document.BuildUri,
                        TargetType: document.RootType,
                        SourcePath: document.SourcePath,
                        SourcePersisted: sourcePersisted,
                        HotReloadObserved: true,
                        RuntimeFallbackApplied: false);
                }

                if (runtimeFallbackApplied)
                {
                    return new SourceGenHotDesignApplyResult(
                        Succeeded: true,
                        Message: "Persisted hot design source update; hot reload timeout reached, runtime fallback was applied.",
                        BuildUri: document.BuildUri,
                        TargetType: document.RootType,
                        SourcePath: document.SourcePath,
                        SourcePersisted: sourcePersisted,
                        HotReloadObserved: false,
                        RuntimeFallbackApplied: true);
                }

                return new SourceGenHotDesignApplyResult(
                    Succeeded: false,
                    Message: "Persisted hot design source update but no hot reload completion was observed before timeout.",
                    BuildUri: document.BuildUri,
                    TargetType: document.RootType,
                    SourcePath: document.SourcePath,
                    SourcePersisted: sourcePersisted,
                    HotReloadObserved: false,
                    RuntimeFallbackApplied: false);
            }

            return new SourceGenHotDesignApplyResult(
                Succeeded: true,
                Message: "Persisted hot design source update.",
                BuildUri: document.BuildUri,
                TargetType: document.RootType,
                SourcePath: document.SourcePath,
                SourcePersisted: sourcePersisted,
                HotReloadObserved: false,
                RuntimeFallbackApplied: false);
        }
    }

    private sealed class Registration(
        Type rootType,
        string buildUri,
        string? sourcePath,
        Action<object> runtimeApplyAction)
    {
        public Type RootType { get; } = rootType;

        public string BuildUri { get; set; } = buildUri;

        public string? SourcePath { get; set; } = sourcePath;

        public Action<object> RuntimeApplyAction { get; set; } = runtimeApplyAction;

        public List<WeakReference<object>> Instances { get; } = [];
    }
}
