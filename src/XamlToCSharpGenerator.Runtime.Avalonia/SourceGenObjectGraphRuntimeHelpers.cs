using System;
using System.Collections;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenObjectGraphRuntimeHelpers
{
    public static Uri? TryCreateUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri)
            ? uri
            : null;
    }

    public static void TryClearCollection(object? value)
    {
        switch (value)
        {
            case null:
                return;
            case IResourceDictionary resourceDictionary:
                try
                {
                    resourceDictionary.Clear();
                }
                catch (NotSupportedException)
                {
                }

                TryClearCollection(resourceDictionary.MergedDictionaries);
                TryClearCollection(resourceDictionary.ThemeDictionaries);
                return;
            case IDictionary dictionary:
                try
                {
                    dictionary.Clear();
                }
                catch (NotSupportedException)
                {
                }

                return;
            case System.Collections.Generic.ICollection<SetterBase> setterCollection:
                TryClearCollectionCore(setterCollection);
                return;
            case System.Collections.Generic.ICollection<IStyle> styleCollection:
                TryClearCollectionCore(styleCollection);
                return;
            case System.Collections.Generic.ICollection<IResourceProvider> resourceProviderCollection:
                TryClearCollectionCore(resourceProviderCollection);
                return;
            case System.Collections.Generic.ICollection<IDataTemplate> dataTemplateCollection:
                TryClearCollectionCore(dataTemplateCollection);
                return;
            case System.Collections.Generic.ICollection<object?> objectCollection:
                TryClearCollectionCore(objectCollection);
                return;
            case System.Collections.Generic.ICollection<string> stringCollection:
                TryClearCollectionCore(stringCollection);
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

    private static void TryClearCollectionCore<T>(System.Collections.Generic.ICollection<T> collection)
    {
        try
        {
            collection.Clear();
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    public static void TryClearDictionaryEntries(object? value)
    {
        switch (value)
        {
            case null:
                return;
            case IResourceDictionary resourceDictionary:
                try
                {
                    resourceDictionary.Clear();
                }
                catch (NotSupportedException)
                {
                }

                return;
            case IDictionary dictionary:
                try
                {
                    dictionary.Clear();
                }
                catch (NotSupportedException)
                {
                }

                return;
        }
    }

    public static bool TryApplyMergedResourceInclude(object? ownerDictionary, object? includeValue, string? documentUri)
    {
        if (ownerDictionary is not IResourceDictionary destinationDictionary ||
            includeValue is not ResourceInclude resourceInclude)
        {
            return false;
        }

        var mergeIntoDictionary = includeValue is MergeResourceInclude;

        object? loadedInclude = null;
        if (!TryResolveResourceInclude(resourceInclude, ownerDictionary, documentUri, out loadedInclude))
        {
            try
            {
                loadedInclude = resourceInclude.Loaded;
            }
            catch
            {
                return false;
            }
        }

        if (loadedInclude is IResourceDictionary mergedResourceDictionary)
        {
            if (mergeIntoDictionary)
            {
                TryMergeDictionary(destinationDictionary, mergedResourceDictionary);
                TryMergeThemeDictionaryMap(destinationDictionary.ThemeDictionaries, mergedResourceDictionary.ThemeDictionaries);
                return true;
            }

            try
            {
                destinationDictionary.MergedDictionaries.Add(mergedResourceDictionary);
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (loadedInclude is IResourceProvider mergedResourceProvider)
        {
            if (mergeIntoDictionary && mergedResourceProvider is IDictionary mergedDictionaryProvider)
            {
                TryMergeDictionary(destinationDictionary, mergedDictionaryProvider);
                return true;
            }

            try
            {
                destinationDictionary.MergedDictionaries.Add(mergedResourceProvider);
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (loadedInclude is IDictionary mergedDictionary)
        {
            TryMergeDictionary(destinationDictionary, mergedDictionary);
            return true;
        }

        return false;
    }

    public static bool TryApplyStyleInclude(object? targetCollection, object? ownerContext, object? includeValue, string? documentUri)
    {
        if (includeValue is not StyleInclude styleInclude)
        {
            return false;
        }

        if (!TryResolveStyleInclude(styleInclude, ownerContext, documentUri, out var resolvedStyle))
        {
            try
            {
                if (styleInclude.Loaded is not IStyle loadedStyle)
                {
                    return false;
                }

                resolvedStyle = loadedStyle;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            switch (targetCollection)
            {
                case Styles styles:
                    styles.Add(resolvedStyle);
                    return true;
                case System.Collections.Generic.ICollection<IStyle> styleCollection:
                    styleCollection.Add(resolvedStyle);
                    return true;
                case IList list when !list.IsReadOnly && !list.IsFixedSize:
                    list.Add(resolvedStyle);
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static void TryMergeDictionary(object? destination, object? source)
    {
        if (destination is System.Collections.Generic.IDictionary<object, object?> destinationDictionaryGeneric &&
            source is System.Collections.Generic.IDictionary<object, object?> sourceDictionaryGeneric)
        {
            foreach (var entry in sourceDictionaryGeneric)
            {
                destinationDictionaryGeneric[entry.Key] = entry.Value;
            }

            return;
        }

        if (destination is not IDictionary destinationDictionary ||
            source is not IDictionary sourceDictionary)
        {
            return;
        }

        foreach (DictionaryEntry entry in sourceDictionary)
        {
            destinationDictionary[entry.Key] = entry.Value;
        }
    }

    public static void TryAddToDictionary(object? dictionary, object? key, object? value, string? documentUri)
    {
        if (dictionary is null || key is null)
        {
            return;
        }

        if (dictionary is ResourceDictionary resourceDictionary &&
            value is IDeferredContent deferredContent)
        {
            try
            {
                if (resourceDictionary.ContainsKey(key))
                {
                    resourceDictionary.Remove(key);
                }

                resourceDictionary.AddNotSharedDeferred(key, deferredContent);
            }
            catch
            {
            }

            return;
        }

        var dictionaryValue = value;
        if (!TryResolveDictionaryEntryValue(dictionary, value, documentUri, out dictionaryValue))
        {
            dictionaryValue = value;
        }

        if (dictionary is System.Collections.Generic.IDictionary<object, object?> genericMap)
        {
            try
            {
                genericMap[key] = dictionaryValue;
            }
            catch
            {
            }

            return;
        }

        if (dictionary is IDictionary map)
        {
            try
            {
                map[key] = dictionaryValue;
            }
            catch
            {
            }
        }
    }

    public static void BeginInit(object? value)
    {
        if (value is ISupportInitialize initialize)
        {
            initialize.BeginInit();
        }
    }

    public static void EndInit(object? value)
    {
        if (value is ISupportInitialize initialize)
        {
            initialize.EndInit();
        }
    }

    public static void TryCompleteNameScope(object? scope)
    {
        if (scope is INameScope nameScope && !nameScope.IsCompleted)
        {
            nameScope.Complete();
        }
    }

    public static void TrySetNameScope(object? target, object? scope)
    {
        if (target is not StyledElement styledElement ||
            scope is not INameScope nameScope)
        {
            return;
        }

        if (NameScope.GetNameScope(styledElement) is null)
        {
            NameScope.SetNameScope(styledElement, nameScope);
        }
    }

    private static bool TryResolveIncludeUri(Uri? source, string? documentUri, out Uri includeUri)
    {
        includeUri = default!;
        if (source is null)
        {
            return false;
        }

        includeUri = source;
        if (includeUri.IsAbsoluteUri)
        {
            return true;
        }

        var baseUri = TryCreateUri(documentUri);
        if (baseUri is null || !baseUri.IsAbsoluteUri)
        {
            return false;
        }

        includeUri = new Uri(baseUri, includeUri);
        return true;
    }

    private static bool TryResolveResourceInclude(
        ResourceInclude resourceInclude,
        object? ownerContext,
        string? documentUri,
        out object? loadedInclude)
    {
        loadedInclude = null;
        if (!TryResolveIncludeUri(resourceInclude.Source, documentUri, out var includeUri))
        {
            return false;
        }

        var includeServiceProvider = CreateIncludeLoadServiceProvider(ownerContext, documentUri);
        if (!AvaloniaSourceGeneratedXamlLoader.TryLoad(includeServiceProvider, includeUri, out var loaded) ||
            loaded is null)
        {
            return false;
        }

        loadedInclude = loaded;
        return true;
    }

    private static bool TryResolveStyleInclude(
        StyleInclude styleInclude,
        object? ownerContext,
        string? documentUri,
        out IStyle resolvedStyle)
    {
        resolvedStyle = default!;
        if (!TryResolveIncludeUri(styleInclude.Source, documentUri, out var includeUri))
        {
            return false;
        }

        var includeServiceProvider = CreateIncludeLoadServiceProvider(ownerContext, documentUri);
        if (!AvaloniaSourceGeneratedXamlLoader.TryLoad(includeServiceProvider, includeUri, out var loaded) ||
            loaded is not IStyle style)
        {
            return false;
        }

        resolvedStyle = style;
        return true;
    }

    private static IServiceProvider CreateIncludeLoadServiceProvider(object? ownerContext, string? documentUri)
    {
        return new SourceGenIncludeLoadServiceProvider(ownerContext, TryCreateUri(documentUri));
    }

    private static void TryMergeThemeDictionaryMap(object? destination, object? source)
    {
        if (destination is not IDictionary destinationMap ||
            source is not IDictionary sourceMap)
        {
            return;
        }

        foreach (DictionaryEntry entry in sourceMap)
        {
            if (destinationMap.Contains(entry.Key))
            {
                var existingValue = destinationMap[entry.Key];
                if (existingValue is IResourceDictionary existingResourceDictionary &&
                    entry.Value is IResourceDictionary sourceResourceDictionary)
                {
                    TryMergeDictionary(existingResourceDictionary, sourceResourceDictionary);
                    continue;
                }

                if (existingValue is IDictionary existingDictionary &&
                    entry.Value is IDictionary sourceDictionary)
                {
                    TryMergeDictionary(existingDictionary, sourceDictionary);
                    continue;
                }
            }

            destinationMap[entry.Key] = entry.Value;
        }
    }

    private static bool TryResolveDictionaryEntryValue(
        object? ownerContext,
        object? value,
        string? documentUri,
        out object? resolvedValue)
    {
        resolvedValue = value;
        if (value is StyleInclude styleInclude)
        {
            if (TryResolveStyleInclude(styleInclude, ownerContext, documentUri, out var resolvedStyle))
            {
                resolvedValue = resolvedStyle;
                return true;
            }

            try
            {
                if (styleInclude.Loaded is IStyle loadedStyle)
                {
                    resolvedValue = loadedStyle;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        if (value is ResourceInclude resourceInclude)
        {
            if (TryResolveResourceInclude(resourceInclude, ownerContext, documentUri, out var loadedInclude) &&
                loadedInclude is not null)
            {
                resolvedValue = loadedInclude;
                return true;
            }

            try
            {
                var loadedValue = resourceInclude.Loaded;
                if (loadedValue is not null)
                {
                    resolvedValue = loadedValue;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        return true;
    }

    private sealed class SourceGenIncludeLoadServiceProvider : IServiceProvider, IUriContext, IAvaloniaXamlIlParentStackProvider
    {
        private readonly object[] _parents;
        private readonly Uri _baseUri;

        public SourceGenIncludeLoadServiceProvider(object? ownerContext, Uri? baseUri)
        {
            _baseUri = baseUri ?? new Uri("avares://sourcegen/");
            _parents = ownerContext is null ? Array.Empty<object>() : [ownerContext];
        }

        public System.Collections.Generic.IEnumerable<object> Parents => _parents;

        public Uri BaseUri
        {
            get => _baseUri;
            set
            {
            }
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IUriContext) ||
                serviceType == typeof(IAvaloniaXamlIlParentStackProvider))
            {
                return this;
            }

            return null;
        }
    }
}
