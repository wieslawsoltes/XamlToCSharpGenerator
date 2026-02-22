using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotReloadStateTracker
{
    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<object, TrackedState> States = new();
    private static readonly Dictionary<string, AvaloniaProperty?> AvaloniaPropertyCache = new(StringComparer.Ordinal);

    public static void Reconcile(
        object instance,
        string[]? collectionMembers,
        string[]? clrPropertyMembers,
        string[]? avaloniaPropertyMembers,
        bool clearSelfCollection,
        SourceGenHotReloadEventDescriptor[]? rootEventSubscriptions = null)
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

        foreach (var member in previousState.CollectionMembers)
        {
            if (currentState.CollectionMembers.Contains(member))
            {
                continue;
            }

            TryClearMemberCollection(instance, member);
        }

        foreach (var member in previousState.ClrPropertyMembers)
        {
            if (currentState.ClrPropertyMembers.Contains(member))
            {
                continue;
            }

            TryResetClrMember(instance, member);
        }

        foreach (var token in previousState.AvaloniaPropertyMembers)
        {
            if (currentState.AvaloniaPropertyMembers.Contains(token))
            {
                continue;
            }

            TryClearAvaloniaProperty(instance, token);
        }

        foreach (var token in previousState.RootEventSubscriptionTokens)
        {
            if (currentState.RootEventSubscriptionTokens.Contains(token))
            {
                continue;
            }

            TryDetachRootEventHandler(instance, token);
        }

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

        TryInvokeClearMethod(value);
    }

    private static void TryClearMemberCollection(object instance, string memberName)
    {
        var value = TryGetMemberValue(instance, memberName);
        TryClearCollection(value);
    }

    private static void TryResetClrMember(object instance, string memberName)
    {
        var type = instance.GetType();
        var propertyInfo = TryGetWritableProperty(type, memberName);
        if (propertyInfo is not null)
        {
            try
            {
                var propertyType = propertyInfo.PropertyType;
                var defaultValue = propertyType.IsValueType
                    ? Activator.CreateInstance(propertyType)
                    : null;
                propertyInfo.SetValue(instance, defaultValue);
            }
            catch
            {
                // Best effort reset only.
            }

            return;
        }

        var fieldInfo = TryGetWritableField(type, memberName);
        if (fieldInfo is null)
        {
            return;
        }

        try
        {
            var fieldType = fieldInfo.FieldType;
            var defaultValue = fieldType.IsValueType
                ? Activator.CreateInstance(fieldType)
                : null;
            fieldInfo.SetValue(instance, defaultValue);
        }
        catch
        {
            // Best effort reset only.
        }
    }

    private static void TryClearAvaloniaProperty(object instance, string propertyToken)
    {
        if (instance is not AvaloniaObject avaloniaObject)
        {
            return;
        }

        var property = ResolveAvaloniaProperty(propertyToken);
        if (property is null)
        {
            return;
        }

        try
        {
            avaloniaObject.ClearValue(property);
        }
        catch
        {
            // Best effort reset only.
        }
    }

    private static object? TryGetMemberValue(object instance, string memberName)
    {
        var normalizedMemberName = NormalizeMemberName(memberName);
        if (string.IsNullOrWhiteSpace(normalizedMemberName))
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();

        var propertyInfo = type.GetProperty(normalizedMemberName, flags);
        if (propertyInfo is not null &&
            propertyInfo.GetIndexParameters().Length == 0)
        {
            try
            {
                return propertyInfo.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        var fieldInfo = type.GetField(normalizedMemberName, flags);
        if (fieldInfo is null)
        {
            return null;
        }

        try
        {
            return fieldInfo.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static PropertyInfo? TryGetWritableProperty(Type type, string memberName)
    {
        var normalizedMemberName = NormalizeMemberName(memberName);
        if (string.IsNullOrWhiteSpace(normalizedMemberName))
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var propertyInfo = type.GetProperty(normalizedMemberName, flags);
        if (propertyInfo is null ||
            propertyInfo.GetIndexParameters().Length != 0 ||
            !propertyInfo.CanWrite ||
            propertyInfo.SetMethod is null ||
            propertyInfo.SetMethod.IsStatic)
        {
            return null;
        }

        return propertyInfo;
    }

    private static FieldInfo? TryGetWritableField(Type type, string memberName)
    {
        var normalizedMemberName = NormalizeMemberName(memberName);
        if (string.IsNullOrWhiteSpace(normalizedMemberName))
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fieldInfo = type.GetField(normalizedMemberName, flags);
        if (fieldInfo is null ||
            fieldInfo.IsStatic ||
            fieldInfo.IsLiteral ||
            fieldInfo.IsInitOnly)
        {
            return null;
        }

        return fieldInfo;
    }

    private static AvaloniaProperty? ResolveAvaloniaProperty(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        lock (Sync)
        {
            if (AvaloniaPropertyCache.TryGetValue(token, out var cached))
            {
                return cached;
            }
        }

        var resolved = ResolveAvaloniaPropertyCore(token);
        lock (Sync)
        {
            AvaloniaPropertyCache[token] = resolved;
        }

        return resolved;
    }

    private static AvaloniaProperty? ResolveAvaloniaPropertyCore(string token)
    {
        var separatorIndex = token.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
        {
            return null;
        }

        var ownerTypeName = token.Substring(0, separatorIndex);
        var fieldName = token.Substring(separatorIndex + 1);
        var ownerType = ResolveType(ownerTypeName);
        if (ownerType is null)
        {
            return null;
        }

        const BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var field = ownerType.GetField(fieldName, fieldFlags);
        if (field is null)
        {
            return null;
        }

        try
        {
            return field.GetValue(null) as AvaloniaProperty;
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolveType(string typeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        if (string.IsNullOrWhiteSpace(normalizedTypeName))
        {
            return null;
        }

        var type = Type.GetType(normalizedTypeName, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(normalizedTypeName, throwOnError: false);
            }
            catch
            {
                type = null;
            }

            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static string NormalizeMemberName(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return string.Empty;
        }

        var trimmed = memberName.Trim();
        var separatorIndex = trimmed.LastIndexOf('.');
        if (separatorIndex < 0 || separatorIndex >= trimmed.Length - 1)
        {
            return trimmed;
        }

        return trimmed.Substring(separatorIndex + 1);
    }

    private static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        var normalized = typeName.Trim();
        const string globalPrefix = "global::";
        if (normalized.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            normalized = normalized.Substring(globalPrefix.Length);
        }

        return normalized;
    }

    private static void TryDetachRootEventHandler(object instance, string token)
    {
        if (!TryParseEventToken(token, out var descriptor))
        {
            return;
        }

        if (descriptor.IsRoutedEvent)
        {
            TryDetachRoutedEventHandler(instance, descriptor);
            return;
        }

        TryDetachClrEventHandler(instance, descriptor);
    }

    private static bool TryParseEventToken(string token, out SourceGenHotReloadEventDescriptor descriptor)
    {
        descriptor = new SourceGenHotReloadEventDescriptor(string.Empty, string.Empty, false, null, null, null);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('|');
        if (parts.Length != 6)
        {
            return false;
        }

        var isRoutedEvent = string.Equals(parts[0], "R", StringComparison.Ordinal);
        descriptor = new SourceGenHotReloadEventDescriptor(
            parts[1],
            parts[2],
            isRoutedEvent,
            NullIfEmpty(parts[3]),
            NullIfEmpty(parts[4]),
            NullIfEmpty(parts[5]));
        return true;
    }

    private static void TryDetachClrEventHandler(object instance, SourceGenHotReloadEventDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.EventName) ||
            string.IsNullOrWhiteSpace(descriptor.HandlerMethodName))
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var eventInfo = instance.GetType().GetEvent(descriptor.EventName, flags);
        if (eventInfo?.EventHandlerType is null)
        {
            return;
        }

        var handler = TryCreateHandlerDelegate(instance, descriptor.HandlerMethodName, eventInfo.EventHandlerType);
        if (handler is null)
        {
            return;
        }

        try
        {
            eventInfo.RemoveEventHandler(instance, handler);
        }
        catch
        {
            // Best effort detach only.
        }
    }

    private static void TryDetachRoutedEventHandler(object instance, SourceGenHotReloadEventDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.RoutedEventOwnerTypeName) ||
            string.IsNullOrWhiteSpace(descriptor.RoutedEventFieldName) ||
            string.IsNullOrWhiteSpace(descriptor.HandlerMethodName))
        {
            return;
        }

        var ownerType = ResolveType(descriptor.RoutedEventOwnerTypeName);
        if (ownerType is null)
        {
            return;
        }

        const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var routedEventField = ownerType.GetField(descriptor.RoutedEventFieldName, staticFlags);
        if (routedEventField is null)
        {
            return;
        }

        object? routedEventValue;
        try
        {
            routedEventValue = routedEventField.GetValue(null);
        }
        catch
        {
            return;
        }

        if (routedEventValue is null)
        {
            return;
        }

        var handlerType = ResolveType(descriptor.RoutedEventHandlerTypeName ?? string.Empty);
        if (handlerType is null)
        {
            return;
        }

        var handler = TryCreateHandlerDelegate(instance, descriptor.HandlerMethodName, handlerType);
        if (handler is null)
        {
            return;
        }

        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo? removeHandlerMethod = null;
        foreach (var candidate in instance.GetType().GetMethods(instanceFlags))
        {
            if (!string.Equals(candidate.Name, "RemoveHandler", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = candidate.GetParameters();
            if (parameters.Length != 2)
            {
                continue;
            }

            if (!parameters[0].ParameterType.IsInstanceOfType(routedEventValue))
            {
                continue;
            }

            if (!typeof(Delegate).IsAssignableFrom(parameters[1].ParameterType))
            {
                continue;
            }

            removeHandlerMethod = candidate;
            break;
        }

        if (removeHandlerMethod is null)
        {
            return;
        }

        try
        {
            removeHandlerMethod.Invoke(instance, [routedEventValue, handler]);
        }
        catch
        {
            // Best effort detach only.
        }
    }

    private static Delegate? TryCreateHandlerDelegate(object instance, string handlerMethodName, Type handlerType)
    {
        if (string.IsNullOrWhiteSpace(handlerMethodName))
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var methodInfo = instance.GetType().GetMethod(handlerMethodName, flags);
        if (methodInfo is null)
        {
            return null;
        }

        try
        {
            return Delegate.CreateDelegate(handlerType, instance, methodInfo, throwOnBindFailure: false);
        }
        catch
        {
            return null;
        }
    }

    private static void TryInvokeClearMethod(object value)
    {
        if (value is null)
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo? clearMethod = null;
        foreach (var candidate in value.GetType().GetMethods(flags))
        {
            if (!string.Equals(candidate.Name, "Clear", StringComparison.Ordinal) ||
                candidate.IsStatic ||
                candidate.IsAbstract ||
                candidate.ReturnType != typeof(void) ||
                candidate.GetParameters().Length != 0)
            {
                continue;
            }

            clearMethod = candidate;
            break;
        }

        if (clearMethod is null)
        {
            return;
        }

        try
        {
            clearMethod.Invoke(value, null);
        }
        catch
        {
            // Best effort clear only.
        }
    }

    private static string BuildEventToken(SourceGenHotReloadEventDescriptor descriptor)
    {
        var kind = descriptor.IsRoutedEvent ? "R" : "C";
        return string.Join("|",
            kind,
            descriptor.EventName ?? string.Empty,
            descriptor.HandlerMethodName ?? string.Empty,
            descriptor.RoutedEventOwnerTypeName ?? string.Empty,
            descriptor.RoutedEventFieldName ?? string.Empty,
            descriptor.RoutedEventHandlerTypeName ?? string.Empty);
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
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
            HashSet<string> collectionMembers,
            HashSet<string> clrPropertyMembers,
            HashSet<string> avaloniaPropertyMembers,
            bool clearSelfCollection,
            HashSet<string> rootEventSubscriptionTokens)
        {
            CollectionMembers = collectionMembers;
            ClrPropertyMembers = clrPropertyMembers;
            AvaloniaPropertyMembers = avaloniaPropertyMembers;
            ClearSelfCollection = clearSelfCollection;
            RootEventSubscriptionTokens = rootEventSubscriptionTokens;
        }

        public HashSet<string> CollectionMembers { get; }

        public HashSet<string> ClrPropertyMembers { get; }

        public HashSet<string> AvaloniaPropertyMembers { get; }

        public bool ClearSelfCollection { get; }

        public HashSet<string> RootEventSubscriptionTokens { get; }

        public static TrackedState Create(
            string[]? collectionMembers,
            string[]? clrPropertyMembers,
            string[]? avaloniaPropertyMembers,
            bool clearSelfCollection,
            SourceGenHotReloadEventDescriptor[]? rootEventSubscriptions)
        {
            return new TrackedState(
                NormalizeMembers(collectionMembers),
                NormalizeMembers(clrPropertyMembers),
                NormalizeTokens(avaloniaPropertyMembers),
                clearSelfCollection,
                NormalizeEventTokens(rootEventSubscriptions));
        }

        private static HashSet<string> NormalizeMembers(string[]? values)
        {
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            if (values is null || values.Length == 0)
            {
                return normalized;
            }

            foreach (var value in values)
            {
                var memberName = NormalizeMemberName(value);
                if (!string.IsNullOrWhiteSpace(memberName))
                {
                    normalized.Add(memberName);
                }
            }

            return normalized;
        }

        private static HashSet<string> NormalizeTokens(string[]? values)
        {
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            if (values is null || values.Length == 0)
            {
                return normalized;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    normalized.Add(value.Trim());
                }
            }

            return normalized;
        }

        private static HashSet<string> NormalizeEventTokens(SourceGenHotReloadEventDescriptor[]? descriptors)
        {
            var normalized = new HashSet<string>(StringComparer.Ordinal);
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

                var token = BuildEventToken(descriptor);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    normalized.Add(token);
                }
            }

            return normalized;
        }
    }
}
