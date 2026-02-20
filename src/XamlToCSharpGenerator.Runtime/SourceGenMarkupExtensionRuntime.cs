using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Platform;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenMarkupExtensionRuntime
{
    public static object? ProvideStaticResource(
        object resourceKey,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        try
        {
            var extension = new StaticResourceExtension(resourceKey);
            var value = extension.ProvideValue(contextProvider);
            if (!ReferenceEquals(value, AvaloniaProperty.UnsetValue))
            {
                return value;
            }

            if (!string.IsNullOrWhiteSpace(baseUri))
            {
                try
                {
                    return SourceGenStaticResourceResolver.Resolve(targetObject, resourceKey, baseUri!, parentStack);
                }
                catch (KeyNotFoundException)
                {
                    // Keep Avalonia delayed-binding behavior if the fallback path cannot resolve yet.
                }
            }

            return value;
        }
        catch (KeyNotFoundException) when (!string.IsNullOrWhiteSpace(baseUri))
        {
            return SourceGenStaticResourceResolver.Resolve(targetObject, resourceKey, baseUri!, parentStack);
        }
    }

    public static T CoerceStaticResourceValue<T>(object? value)
    {
        if (ReferenceEquals(value, AvaloniaProperty.UnsetValue) || value is null)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        return (T)value;
    }

    public static IBinding? ProvideDynamicResource(
        string resourceKey,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var extension = new DynamicResourceExtension(resourceKey);
        return extension.ProvideValue(contextProvider);
    }

    public static IBinding? ProvideReflectionBinding(
        ReflectionBindingExtension extension,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        return extension.ProvideValue(contextProvider);
    }

    public static IBinding ProvideExpressionBinding<TSource>(
        Func<TSource, object?> evaluator,
        IReadOnlyList<string>? dependencyNames,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
        where TSource : class
    {
        _ = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var multiBinding = new MultiBinding
        {
            Converter = new SourceGenExpressionMultiValueConverter<TSource>(evaluator),
            Mode = BindingMode.OneWay
        };

        // First binding carries the source object used by the generated evaluator delegate.
        multiBinding.Bindings.Add(new Binding("."));

        if (dependencyNames is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependencyName in dependencyNames)
            {
                if (string.IsNullOrWhiteSpace(dependencyName))
                {
                    continue;
                }

                var trimmedDependencyName = dependencyName.Trim();
                if (!seen.Add(trimmedDependencyName))
                {
                    continue;
                }

                multiBinding.Bindings.Add(new Binding(trimmedDependencyName));
            }
        }

        return multiBinding;
    }

    public static object? ProvideMarkupExtension(
        MarkupExtension extension,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        return extension.ProvideValue(contextProvider);
    }

    public static object? ProvideReference(
        string name,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var extension = new ResolveByNameExtension(name);
        var value = extension.ProvideValue(contextProvider);
        if (ReferenceEquals(value, AvaloniaProperty.UnsetValue))
        {
            return SourceGenNameReferenceHelper.ResolveByName(targetObject, name);
        }

        return value;
    }

    public static object? ProvideOnPlatform(
        object? defaultValue,
        object? windows,
        object? macOs,
        object? linux,
        object? android,
        object? ios,
        object? browser)
    {
        if (OperatingSystem.IsWindows() && windows is not null)
        {
            return windows;
        }

        if (OperatingSystem.IsMacOS() && macOs is not null)
        {
            return macOs;
        }

        if (OperatingSystem.IsLinux() && linux is not null)
        {
            return linux;
        }

        if (OperatingSystem.IsAndroid() && android is not null)
        {
            return android;
        }

        if (OperatingSystem.IsIOS() && ios is not null)
        {
            return ios;
        }

        if (OperatingSystem.IsBrowser() && browser is not null)
        {
            return browser;
        }

        return defaultValue;
    }

    public static object? ProvideOnFormFactor(
        object? defaultValue,
        object? desktop,
        object? mobile,
        object? tv,
        IServiceProvider? parentServiceProvider)
    {
        var serviceProvider = parentServiceProvider ?? EmptyServiceProvider.Instance;
        if (desktop is not null &&
            OnFormFactorExtension.ShouldProvideOption(serviceProvider, FormFactorType.Desktop))
        {
            return desktop;
        }

        if (mobile is not null &&
            OnFormFactorExtension.ShouldProvideOption(serviceProvider, FormFactorType.Mobile))
        {
            return mobile;
        }

        return defaultValue;
    }

    private static MarkupExtensionServiceProvider CreateContextProvider(
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        return new MarkupExtensionServiceProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            ResolveBaseUri(parentServiceProvider, baseUri),
            BuildParentStack(targetObject, parentStack));
    }

    private static Uri ResolveBaseUri(IServiceProvider? parentServiceProvider, string? baseUri)
    {
        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext parentUriContext)
        {
            return parentUriContext.BaseUri;
        }

        if (!string.IsNullOrWhiteSpace(baseUri) &&
            Uri.TryCreate(baseUri, UriKind.RelativeOrAbsolute, out var resolvedBaseUri))
        {
            return resolvedBaseUri;
        }

        return new Uri("avares://sourcegen/");
    }

    private static object[] BuildParentStack(object targetObject, IReadOnlyList<object>? parentStack)
    {
        if (parentStack is null || parentStack.Count == 0)
        {
            return [targetObject];
        }

        var stack = new object[parentStack.Count];
        for (var index = 0; index < parentStack.Count; index++)
        {
            stack[index] = parentStack[index];
        }

        return stack;
    }

    private sealed class MarkupExtensionServiceProvider :
        IServiceProvider,
        IProvideValueTarget,
        IRootObjectProvider,
        IUriContext,
        IAvaloniaXamlIlParentStackProvider
    {
        private readonly IServiceProvider? _parentServiceProvider;
        private readonly object _rootObject;
        private readonly object _intermediateRootObject;
        private readonly object _targetObject;
        private readonly object? _targetProperty;
        private readonly object[] _parentStack;
        private readonly Uri _baseUri;

        public MarkupExtensionServiceProvider(
            IServiceProvider? parentServiceProvider,
            object rootObject,
            object intermediateRootObject,
            object targetObject,
            object? targetProperty,
            Uri baseUri,
            object[] parentStack)
        {
            _parentServiceProvider = parentServiceProvider;
            _rootObject = rootObject;
            _intermediateRootObject = intermediateRootObject;
            _targetObject = targetObject;
            _targetProperty = targetProperty;
            _baseUri = baseUri;
            _parentStack = parentStack;
        }

        public object TargetObject => _targetObject;

        public object TargetProperty => _targetProperty ?? AvaloniaProperty.UnsetValue;

        public object RootObject => _rootObject;

        public object IntermediateRootObject => _intermediateRootObject;

        public Uri BaseUri
        {
            get => _baseUri;
            set { }
        }

        public IEnumerable<object> Parents => EnumerateParents();

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IProvideValueTarget))
            {
                return this;
            }

            if (serviceType == typeof(IRootObjectProvider))
            {
                return this;
            }

            if (serviceType == typeof(IUriContext))
            {
                return this;
            }

            if (serviceType == typeof(IAvaloniaXamlIlParentStackProvider))
            {
                return this;
            }

            if (serviceType == typeof(INameScope))
            {
                if (_parentServiceProvider?.GetService(typeof(INameScope)) is INameScope nameScope)
                {
                    return nameScope;
                }

                if (_targetObject is StyledElement styledElement &&
                    NameScope.GetNameScope(styledElement) is { } elementNameScope)
                {
                    return elementNameScope;
                }
            }

            return _parentServiceProvider?.GetService(serviceType);
        }

        private IEnumerable<object> EnumerateParents()
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < _parentStack.Length; index++)
            {
                var parent = _parentStack[index];
                if (seen.Add(parent))
                {
                    yield return parent;
                }
            }

            if (_parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) is IAvaloniaXamlIlParentStackProvider upstreamStack)
            {
                foreach (var upstreamParent in upstreamStack.Parents)
                {
                    if (seen.Add(upstreamParent))
                    {
                        yield return upstreamParent;
                    }
                }
            }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
