using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotReloadStateTracker
{
    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<object, TrackedState> States = new();

    public static void Reconcile(
        object instance,
        SourceGenHotReloadCleanupDescriptor[]? collectionMembers,
        SourceGenHotReloadCleanupDescriptor[]? clrPropertyMembers,
        SourceGenHotReloadCleanupDescriptor[]? avaloniaPropertyMembers,
        bool clearSelfCollection,
        SourceGenHotReloadCleanupDescriptor[]? rootEventSubscriptions = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var currentState = TrackedState.Create(
            collectionMembers,
            clrPropertyMembers,
            avaloniaPropertyMembers,
            clearSelfCollection,
            rootEventSubscriptions);

        TrackedState? previousState;
        lock (Sync)
        {
            States.TryGetValue(instance, out previousState);
            States.Remove(instance);
            States.Add(instance, currentState);
        }

        if (previousState is null)
        {
            return;
        }

        ReconcileCategory(previousState.CollectionMembers, currentState.CollectionMembers, instance);
        ReconcileCategory(previousState.ClrPropertyMembers, currentState.ClrPropertyMembers, instance);
        ReconcileCategory(previousState.AvaloniaPropertyMembers, currentState.AvaloniaPropertyMembers, instance);
        ReconcileCategory(previousState.RootEventSubscriptionActions, currentState.RootEventSubscriptionActions, instance);

        if (previousState.ClearSelfCollection && !currentState.ClearSelfCollection)
        {
            TryClearCollection(instance);
        }
    }

    public static void TryClearCollection(object? value)
    {
        switch (value)
        {
            case null:
                return;
            case IResourceDictionary resourceDictionary:
                TryClearResourceDictionary(resourceDictionary);
                return;
            case Styles styles:
                TryClearStyles(styles);
                return;
            case DataTemplates dataTemplates:
                TryClearDataTemplates(dataTemplates);
                return;
            case Classes classes:
                TryClearClasses(classes);
                return;
            case IDictionary dictionary:
                try
                {
                    dictionary.Clear();
                }
                catch (InvalidOperationException)
                {
                }
                catch (NotSupportedException)
                {
                }

                return;
            case IList list:
                try
                {
                    if (list.IsReadOnly || list.IsFixedSize)
                    {
                        return;
                    }

                    list.Clear();
                }
                catch (InvalidOperationException)
                {
                }
                catch (NotSupportedException)
                {
                }

                return;
        }
    }

    private static void ReconcileCategory(
        IReadOnlyDictionary<string, SourceGenHotReloadCleanupDescriptor> previous,
        IReadOnlyDictionary<string, SourceGenHotReloadCleanupDescriptor> current,
        object instance)
    {
        foreach (var pair in previous)
        {
            if (current.ContainsKey(pair.Key))
            {
                continue;
            }

            TryInvokeCleanupAction(pair.Value, instance);
        }
    }

    private static void TryInvokeCleanupAction(SourceGenHotReloadCleanupDescriptor descriptor, object instance)
    {
        var action = descriptor.CleanupAction;
        if (action is null)
        {
            return;
        }

        try
        {
            action(instance);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void TryClearResourceDictionary(IResourceDictionary resourceDictionary)
    {
        try
        {
            resourceDictionary.Clear();
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        TryClearCollection(resourceDictionary.MergedDictionaries);
        TryClearCollection(resourceDictionary.ThemeDictionaries);
    }

    private static void TryClearStyles(Styles styles)
    {
        try
        {
            styles.Clear();
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        TryClearCollection(styles.Resources);
    }

    private static void TryClearDataTemplates(DataTemplates templates)
    {
        try
        {
            templates.Clear();
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void TryClearClasses(Classes classes)
    {
        try
        {
            classes.Clear();
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private sealed class TrackedState
    {
        private TrackedState(
            Dictionary<string, SourceGenHotReloadCleanupDescriptor> collectionMembers,
            Dictionary<string, SourceGenHotReloadCleanupDescriptor> clrPropertyMembers,
            Dictionary<string, SourceGenHotReloadCleanupDescriptor> avaloniaPropertyMembers,
            bool clearSelfCollection,
            Dictionary<string, SourceGenHotReloadCleanupDescriptor> rootEventSubscriptionActions)
        {
            CollectionMembers = collectionMembers;
            ClrPropertyMembers = clrPropertyMembers;
            AvaloniaPropertyMembers = avaloniaPropertyMembers;
            ClearSelfCollection = clearSelfCollection;
            RootEventSubscriptionActions = rootEventSubscriptionActions;
        }

        public Dictionary<string, SourceGenHotReloadCleanupDescriptor> CollectionMembers { get; }

        public Dictionary<string, SourceGenHotReloadCleanupDescriptor> ClrPropertyMembers { get; }

        public Dictionary<string, SourceGenHotReloadCleanupDescriptor> AvaloniaPropertyMembers { get; }

        public bool ClearSelfCollection { get; }

        public Dictionary<string, SourceGenHotReloadCleanupDescriptor> RootEventSubscriptionActions { get; }

        public static TrackedState Create(
            SourceGenHotReloadCleanupDescriptor[]? collectionMembers,
            SourceGenHotReloadCleanupDescriptor[]? clrPropertyMembers,
            SourceGenHotReloadCleanupDescriptor[]? avaloniaPropertyMembers,
            bool clearSelfCollection,
            SourceGenHotReloadCleanupDescriptor[]? rootEventSubscriptions)
        {
            return new TrackedState(
                NormalizeDescriptors(collectionMembers),
                NormalizeDescriptors(clrPropertyMembers),
                NormalizeDescriptors(avaloniaPropertyMembers),
                clearSelfCollection,
                NormalizeDescriptors(rootEventSubscriptions));
        }

        private static Dictionary<string, SourceGenHotReloadCleanupDescriptor> NormalizeDescriptors(
            SourceGenHotReloadCleanupDescriptor[]? descriptors)
        {
            var normalized = new Dictionary<string, SourceGenHotReloadCleanupDescriptor>(StringComparer.Ordinal);
            if (descriptors is null || descriptors.Length == 0)
            {
                return normalized;
            }

            foreach (var descriptor in descriptors)
            {
                if (descriptor is null)
                {
                    continue;
                }

                var token = NormalizeToken(descriptor.Token);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                normalized[token] = descriptor;
            }

            return normalized;
        }

        private static string NormalizeToken(string? token)
        {
            return string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
        }
    }
}
