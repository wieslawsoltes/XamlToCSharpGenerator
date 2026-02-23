using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
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
    private static readonly ConcurrentDictionary<string, Type?> BindingTypeCache = new(StringComparer.Ordinal);

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

        var extension = new StaticResourceExtension(resourceKey);
        return ProvideStaticResourceExtension(
            extension,
            targetObject,
            baseUri,
            parentStack,
            contextProvider);
    }

    public static T CoerceStaticResourceValue<T>(object? value)
    {
        return CoerceMarkupExtensionValue<T>(value);
    }

    public static T CoerceMarkupExtensionValue<T>(object? value)
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

    public static object? ProvideRuntimeXamlValue(
        string xaml,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return null;
        }

        var normalizedXaml = NormalizeRuntimeXamlValue(xaml);
        if (string.IsNullOrWhiteSpace(normalizedXaml))
        {
            return null;
        }

        var localAssembly = rootObject?.GetType().Assembly
            ?? intermediateRootObject?.GetType().Assembly
            ?? targetObject?.GetType().Assembly
            ?? typeof(SourceGenMarkupExtensionRuntime).Assembly;
        var localAssemblyName = localAssembly.GetName().Name ?? "sourcegen";
        var resolvedBaseUri = ResolveRuntimeBaseUri(parentServiceProvider, baseUri, localAssemblyName);

        var options = AvaloniaSourceGeneratedXamlLoader.RuntimeCompilationOptions;
        options.EnableRuntimeCompilationFallback = true;
        options.StrictMode = false;

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly
        };

        var document = new RuntimeXamlLoaderDocument(resolvedBaseUri, rootInstance: null, normalizedXaml)
        {
            ServiceProvider = parentServiceProvider
        };

        return SourceGenRuntimeXamlCompiler.Load(document, configuration, options);
    }

    public static IBinding? ProvideDynamicResource(
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

        var extension = new DynamicResourceExtension(resourceKey);
        return extension.ProvideValue(contextProvider);
    }

    public static T? AttachBindingNameScope<T>(T? binding, object? nameScope)
        where T : class
    {
        if (binding is Binding dataBinding && dataBinding.TypeResolver is null)
        {
            dataBinding.TypeResolver = ResolveBindingType;
        }

        if (binding is BindingBase bindingBase &&
            nameScope is INameScope typedNameScope)
        {
            bindingBase.NameScope = new WeakReference<INameScope?>(typedNameScope);
        }

        return binding;
    }

    public static void ApplyBinding(object? target, AvaloniaProperty property, object? value)
    {
        if (target is not AvaloniaObject avaloniaObject || property is null)
        {
            return;
        }

        if (value is IBinding binding)
        {
            avaloniaObject.Bind(property, binding);
            return;
        }

        avaloniaObject.SetValue(property, value);
    }

    private static Type ResolveBindingType(string? xmlNamespace, string name)
    {
        var key = (xmlNamespace ?? string.Empty) + "|" + name;
        var resolvedType = BindingTypeCache.GetOrAdd(key, static cacheKey =>
        {
            var separatorIndex = cacheKey.IndexOf('|');
            var namespacePart = separatorIndex >= 0 ? cacheKey[..separatorIndex] : string.Empty;
            var typeNamePart = separatorIndex >= 0 ? cacheKey[(separatorIndex + 1)..] : cacheKey;
            return ResolveBindingTypeCore(namespacePart, typeNamePart);
        });

        if (resolvedType is not null)
        {
            return resolvedType;
        }

        throw new InvalidOperationException($"Unable to resolve type '{xmlNamespace}:{name}'.");
    }

    private static Type? ResolveBindingTypeCore(string? xmlNamespace, string name)
    {
        return SourceGenKnownTypeRegistry.TryResolve(xmlNamespace, name, out var resolvedType)
            ? resolvedType
            : null;
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
        object extension,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        return extension switch
        {
            StaticResourceExtension staticResourceExtension => ProvideStaticResourceExtension(
                staticResourceExtension,
                targetObject,
                baseUri,
                parentStack,
                contextProvider),
            DynamicResourceExtension dynamicResourceExtension => dynamicResourceExtension.ProvideValue(contextProvider),
            ResolveByNameExtension resolveByNameExtension => resolveByNameExtension.ProvideValue(contextProvider),
            RelativeSourceExtension relativeSourceExtension => relativeSourceExtension.ProvideValue(contextProvider),
            ReflectionBindingExtension reflectionBindingExtension => reflectionBindingExtension.ProvideValue(contextProvider),
            CompiledBindingExtension compiledBindingExtension => compiledBindingExtension.ProvideValue(contextProvider),
            MarkupExtension markupExtension => markupExtension.ProvideValue(contextProvider),
            _ => throw new NotSupportedException(
                $"Unsupported markup extension type '{extension.GetType().FullName}'.")
        };
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

        if (defaultValue is null)
        {
            if (desktop is not null &&
                (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()))
            {
                return desktop;
            }

            if (mobile is not null &&
                (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()))
            {
                return mobile;
            }

            if (tv is not null && desktop is null && mobile is null)
            {
                return tv;
            }

            return desktop ?? mobile ?? tv;
        }

        return defaultValue;
    }

    private static bool TryResolveStaticResourceFallback(
        object resourceKey,
        object targetObject,
        string? baseUri,
        IServiceProvider? serviceProvider,
        IReadOnlyList<object>? parentStack,
        out object? resolved)
    {
        if (resourceKey is null ||
            ReferenceEquals(resourceKey, AvaloniaProperty.UnsetValue))
        {
            resolved = null;
            return false;
        }

        if (SourceGenStaticResourceResolver.TryResolve(
                targetObject,
                resourceKey,
                baseUri,
                out resolved,
                serviceProvider,
                parentStack))
        {
            return true;
        }

        resolved = null;
        return false;
    }

    private static object? ProvideStaticResourceExtension(
        StaticResourceExtension extension,
        object targetObject,
        string? baseUri,
        IReadOnlyList<object>? parentStack,
        IServiceProvider serviceProvider)
    {
        var resourceKey = extension.ResourceKey ?? AvaloniaProperty.UnsetValue;
        var effectiveParentStack = BuildEffectiveParentStack(serviceProvider, parentStack);

        if (TryResolveStaticResourceFallback(
                resourceKey,
                targetObject,
                baseUri,
                serviceProvider,
                effectiveParentStack,
                out var eagerlyResolved))
        {
            return eagerlyResolved;
        }

        try
        {
            var value = extension.ProvideValue(serviceProvider);
            if (!ReferenceEquals(value, AvaloniaProperty.UnsetValue))
            {
                return value;
            }

            if (TryResolveStaticResourceFallback(
                    resourceKey,
                    targetObject,
                    baseUri,
                    serviceProvider,
                    effectiveParentStack,
                    out var resolvedFallback))
            {
                return resolvedFallback;
            }

            // Keep delayed resource-resolution behavior for control target properties.
            return AvaloniaProperty.UnsetValue;
        }
        catch (KeyNotFoundException)
        {
            if (TryResolveStaticResourceFallback(
                    resourceKey,
                    targetObject,
                    baseUri,
                    serviceProvider,
                    effectiveParentStack,
                    out var resolvedFallback))
            {
                return resolvedFallback;
            }

            throw;
        }
    }

    private static IReadOnlyList<object>? BuildEffectiveParentStack(
        IServiceProvider? parentServiceProvider,
        IReadOnlyList<object>? explicitParentStack)
    {
        List<object>? parents = null;
        HashSet<object>? seen = null;

        void AddParent(object? parent)
        {
            if (parent is null)
            {
                return;
            }

            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!seen.Add(parent))
            {
                return;
            }

            parents ??= new List<object>();
            parents.Add(parent);
        }

        if (explicitParentStack is not null)
        {
            for (var index = 0; index < explicitParentStack.Count; index++)
            {
                AddParent(explicitParentStack[index]);
            }
        }

        if (parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) is IAvaloniaXamlIlParentStackProvider upstreamStack)
        {
            foreach (var upstreamParent in upstreamStack.Parents)
            {
                AddParent(upstreamParent);
            }
        }

        return parents;
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

    private static string NormalizeRuntimeXamlValue(string xaml)
    {
        var trimmed = xaml.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '<')
        {
            return trimmed;
        }

        try
        {
            var element = XElement.Parse(trimmed, LoadOptions.PreserveWhitespace);
            if (element.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal) &&
                element.Elements().FirstOrDefault() is { } innerValue)
            {
                return innerValue.ToString(SaveOptions.DisableFormatting);
            }
        }
        catch
        {
            // Keep original value when fragment parsing fails; downstream loader reports details.
        }

        return trimmed;
    }

    private static Uri ResolveRuntimeBaseUri(
        IServiceProvider? parentServiceProvider,
        string? baseUri,
        string localAssemblyName)
    {
        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext parentUriContext)
        {
            return parentUriContext.BaseUri;
        }

        if (!string.IsNullOrWhiteSpace(baseUri))
        {
            var trimmed = baseUri.Trim();
            if (Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out var resolved))
            {
                if (resolved.IsAbsoluteUri)
                {
                    return resolved;
                }

                var normalizedPath = trimmed.Replace('\\', '/').TrimStart('/');
                if (normalizedPath.Length > 0)
                {
                    return new Uri("avares://" + localAssemblyName + "/" + normalizedPath);
                }
            }
        }

        return new Uri("avares://" + localAssemblyName + "/");
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
