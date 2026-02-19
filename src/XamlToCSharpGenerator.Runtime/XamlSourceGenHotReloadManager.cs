using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using Avalonia.Threading;

[assembly: MetadataUpdateHandler(typeof(XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotReloadManager
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Type, List<WeakReference<object>>> Instances = new();
    private static readonly Dictionary<Type, Action<object>> Reloaders = new();

    public static event Action<Type[]?>? HotReloaded;

    public static event Action<Type, Exception>? HotReloadFailed;

    public static bool IsEnabled { get; private set; } = true;

    public static void Enable()
    {
        IsEnabled = true;
    }

    public static void Disable()
    {
        IsEnabled = false;
    }

    public static void Register(object instance, Action<object> reloadAction)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(reloadAction);

        var type = NormalizeType(instance.GetType());

        lock (Sync)
        {
            Reloaders[type] = reloadAction;

            if (!Instances.TryGetValue(type, out var references))
            {
                references = new List<WeakReference<object>>();
                Instances[type] = references;
            }

            PruneDeadReferences(references);
            if (!ContainsReference(references, instance))
            {
                references.Add(new WeakReference<object>(instance));
            }
        }
    }

    public static void ClearRegistrations()
    {
        lock (Sync)
        {
            Instances.Clear();
            Reloaders.Clear();
        }
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
                var normalizedType = NormalizeType(type);
                if (Instances.TryGetValue(normalizedType, out var references))
                {
                    PruneDeadReferences(references);
                }
            }
        }
    }

    public static void UpdateApplication(Type[]? types)
    {
        if (!IsEnabled)
        {
            HotReloaded?.Invoke(types);
            return;
        }

        var operations = CollectReloadOperations(types);
        foreach (var operation in operations)
        {
            ExecuteReload(operation.Type, operation.Instance, operation.ReloadAction);
        }

        HotReloaded?.Invoke(types);
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
                    if (!Reloaders.TryGetValue(pair.Key, out var reloadAction))
                    {
                        continue;
                    }

                    AddOperationsForType(pair.Key, pair.Value, reloadAction, operations);
                }

                return operations;
            }

            foreach (var type in types)
            {
                var normalizedType = NormalizeType(type);
                if (!Instances.TryGetValue(normalizedType, out var references))
                {
                    continue;
                }

                if (!Reloaders.TryGetValue(normalizedType, out var reloadAction))
                {
                    continue;
                }

                AddOperationsForType(normalizedType, references, reloadAction, operations);
            }

            return operations;
        }
    }

    private static void AddOperationsForType(
        Type type,
        List<WeakReference<object>> references,
        Action<object> reloadAction,
        List<ReloadOperation> operations)
    {
        for (var index = references.Count - 1; index >= 0; index--)
        {
            if (!references[index].TryGetTarget(out var instance) || instance is null)
            {
                references.RemoveAt(index);
                continue;
            }

            operations.Add(new ReloadOperation(type, instance, reloadAction));
        }
    }

    private static void ExecuteReload(Type type, object instance, Action<object> reloadAction)
    {
        void RunReload()
        {
            try
            {
                reloadAction(instance);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"XAML source generator hot reload failed for '{type.FullName}': {ex}");
                HotReloadFailed?.Invoke(type, ex);
            }
        }

        if (instance is not global::Avalonia.AvaloniaObject)
        {
            RunReload();
            return;
        }

        try
        {
            var uiThread = Dispatcher.UIThread;
            if (uiThread.CheckAccess())
            {
                RunReload();
            }
            else
            {
                _ = uiThread.InvokeAsync(RunReload, DispatcherPriority.Background);
            }
        }
        catch
        {
            RunReload();
        }
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

    private readonly record struct ReloadOperation(Type Type, object Instance, Action<object> ReloadAction);
}
