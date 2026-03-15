using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Markup.Xaml.XamlIl.Runtime;
using global::Avalonia.Utilities;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenDeferredServiceProviderFactory
{
    [ThreadStatic]
    private static List<object>? s_parentResourceNodeBuffer;

    [ThreadStatic]
    private static LastParentResourceStack? s_lastParentResourceStack;

    public static INameScope CreateTemplateNameScope(IServiceProvider? serviceProvider)
    {
        _ = serviceProvider;
        return new NameScope();
    }

    public static INameScope CreateDeferredResourceNameScope(IServiceProvider? serviceProvider)
    {
        _ = serviceProvider;
        return new NameScope();
    }

    public static IServiceProvider CreateDeferredTemplateServiceProvider(
        IServiceProvider? parentServiceProvider,
        object rootObject,
        INameScope templateNameScope)
    {
        var upstreamParentStackProvider = parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) as IAvaloniaXamlIlParentStackProvider;
        var parentResourceNodes = CollectParentResourceNodes(upstreamParentStackProvider);
        return new DeferredTemplateServiceProvider(
            parentServiceProvider,
            upstreamParentStackProvider,
            parentResourceNodes,
            rootObject,
            templateNameScope);
    }

    /// <summary>
    /// Creates the ambient services used when a deferred resource entry is materialized.
    /// </summary>
    public static IServiceProvider CreateDeferredResourceServiceProvider(
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        string? baseUri,
        IReadOnlyList<object>? parentStack,
        INameScope? resourceNameScope = null)
    {
        var upstreamParentStackProvider = parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) as IAvaloniaXamlIlParentStackProvider;
        var parentResourceNodes = CollectDeferredResourceNodes(upstreamParentStackProvider, parentStack);
        var resolvedBaseUri = ResolveBaseUri(parentServiceProvider, baseUri, rootObject);
        return new DeferredResourceServiceProvider(
            parentServiceProvider,
            parentResourceNodes,
            rootObject,
            intermediateRootObject,
            resolvedBaseUri,
            resourceNameScope);
    }

    private static object[] CollectParentResourceNodes(IAvaloniaXamlIlParentStackProvider? parentStackProvider)
    {
        if (parentStackProvider is null)
        {
            return Array.Empty<object>();
        }

        var resourceNodes = s_parentResourceNodeBuffer ??= new List<object>(8);
        resourceNodes.Clear();
        foreach (var parent in parentStackProvider.Parents)
        {
            if (parent is IResourceNode)
            {
                resourceNodes.Add(parent);
            }
        }

        resourceNodes.Reverse();

        if (resourceNodes.Count == 0)
        {
            return Array.Empty<object>();
        }

        var lastParentResourceStack = s_lastParentResourceStack;
        if (lastParentResourceStack is not null &&
            lastParentResourceStack.IsEquivalentTo(parentStackProvider, resourceNodes, out var cached))
        {
            resourceNodes.Clear();
            return cached;
        }

        var snapshot = resourceNodes.ToArray();
        resourceNodes.Clear();

        if (lastParentResourceStack is null)
        {
            lastParentResourceStack = new LastParentResourceStack();
            s_lastParentResourceStack = lastParentResourceStack;
        }

        lastParentResourceStack.Set(parentStackProvider, snapshot);
        return snapshot;
    }

    private static object[] CollectDeferredResourceNodes(
        IAvaloniaXamlIlParentStackProvider? parentStackProvider,
        IReadOnlyList<object>? explicitParentStack)
    {
        if ((explicitParentStack is null || explicitParentStack.Count == 0) &&
            parentStackProvider is null)
        {
            return Array.Empty<object>();
        }

        var resourceNodes = s_parentResourceNodeBuffer ??= new List<object>(8);
        resourceNodes.Clear();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        if (explicitParentStack is not null)
        {
            for (var index = 0; index < explicitParentStack.Count; index++)
            {
                var parent = explicitParentStack[index];
                if (parent is IResourceNode && seen.Add(parent))
                {
                    resourceNodes.Add(parent);
                }
            }
        }

        if (parentStackProvider is not null)
        {
            foreach (var parent in parentStackProvider.Parents)
            {
                if (parent is IResourceNode && seen.Add(parent))
                {
                    resourceNodes.Add(parent);
                }
            }
        }

        if (resourceNodes.Count == 0)
        {
            resourceNodes.Clear();
            return Array.Empty<object>();
        }

        var snapshot = resourceNodes.ToArray();
        resourceNodes.Clear();
        return snapshot;
    }

    private static Uri ResolveBaseUri(IServiceProvider? parentServiceProvider, string? baseUri, object rootObject)
    {
        if (SourceGenObjectGraphRuntimeHelpers.TryCreateUri(baseUri) is { } resolvedBaseUri)
        {
            return resolvedBaseUri;
        }

        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext uriContext &&
            uriContext.BaseUri is not null)
        {
            return uriContext.BaseUri;
        }

        var assemblyName = rootObject.GetType().Assembly.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            return new Uri("avares://" + assemblyName + "/");
        }

        return new Uri("avares://Application/");
    }

    private sealed class DeferredTemplateServiceProvider : IServiceProvider, IRootObjectProvider, IAvaloniaXamlIlControlTemplateProvider, IAvaloniaXamlIlParentStackProvider
    {
        private readonly IServiceProvider? _parentServiceProvider;
        private readonly IAvaloniaXamlIlParentStackProvider? _upstreamParentStackProvider;
        private readonly object[] _parentResourceNodes;
        private readonly INameScope _templateNameScope;

        public DeferredTemplateServiceProvider(
            IServiceProvider? parentServiceProvider,
            IAvaloniaXamlIlParentStackProvider? upstreamParentStackProvider,
            object[] parentResourceNodes,
            object rootObject,
            INameScope templateNameScope)
        {
            _parentServiceProvider = parentServiceProvider;
            _upstreamParentStackProvider = upstreamParentStackProvider;
            _parentResourceNodes = parentResourceNodes;
            _templateNameScope = templateNameScope;
            RootObject = rootObject;
        }

        public object RootObject { get; }

        public object IntermediateRootObject => RootObject;

        public IEnumerable<object> Parents
        {
            get
            {
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                for (var index = _parentResourceNodes.Length - 1; index >= 0; index--)
                {
                    var parent = _parentResourceNodes[index];
                    if (seen.Add(parent))
                    {
                        yield return parent;
                    }
                }

                if (_upstreamParentStackProvider is null)
                {
                    yield break;
                }

                foreach (var parent in _upstreamParentStackProvider.Parents)
                {
                    if (seen.Add(parent))
                    {
                        yield return parent;
                    }
                }
            }
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(INameScope))
            {
                return _templateNameScope;
            }

            if (serviceType == typeof(IAvaloniaXamlIlParentStackProvider))
            {
                return this;
            }

            if (serviceType == typeof(IRootObjectProvider))
            {
                return this;
            }

            if (serviceType == typeof(IAvaloniaXamlIlControlTemplateProvider))
            {
                return this;
            }

            return _parentServiceProvider?.GetService(serviceType);
        }
    }

    private sealed class DeferredResourceServiceProvider : IServiceProvider, IRootObjectProvider, IUriContext, IAvaloniaXamlIlParentStackProvider
    {
        private readonly IServiceProvider? _parentServiceProvider;
        private readonly object[] _parentResourceNodes;
        private readonly Uri _baseUri;
        private readonly INameScope? _resourceNameScope;

        public DeferredResourceServiceProvider(
            IServiceProvider? parentServiceProvider,
            object[] parentResourceNodes,
            object rootObject,
            object intermediateRootObject,
            Uri baseUri,
            INameScope? resourceNameScope)
        {
            _parentServiceProvider = parentServiceProvider;
            _parentResourceNodes = parentResourceNodes;
            RootObject = rootObject;
            IntermediateRootObject = intermediateRootObject;
            _baseUri = baseUri;
            _resourceNameScope = resourceNameScope;
        }

        public object RootObject { get; }

        public object IntermediateRootObject { get; }

        public Uri BaseUri
        {
            get => _baseUri;
            set { }
        }

        public IEnumerable<object> Parents => _parentResourceNodes;

        public object? GetService(Type serviceType)
        {
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
                return _resourceNameScope ?? _parentServiceProvider?.GetService(serviceType);
            }

            return _parentServiceProvider?.GetService(serviceType);
        }
    }
    private sealed class LastParentResourceStack
    {
        private readonly WeakReference<IAvaloniaXamlIlParentStackProvider?> _provider = new(null);
        private readonly WeakReference<object[]?> _resourceNodes = new(null);

        public void Set(IAvaloniaXamlIlParentStackProvider provider, object[] resourceNodes)
        {
            _provider.SetTarget(provider);
            _resourceNodes.SetTarget(resourceNodes);
        }

        public bool IsEquivalentTo(
            IAvaloniaXamlIlParentStackProvider provider,
            List<object> resourceNodes,
            out object[] cachedResourceNodes)
        {
            cachedResourceNodes = Array.Empty<object>();
            if (!_provider.TryGetTarget(out var cachedProvider) ||
                !_resourceNodes.TryGetTarget(out var cachedNodes) ||
                !ReferenceEquals(provider, cachedProvider) ||
                cachedNodes is null ||
                resourceNodes.Count != cachedNodes.Length)
            {
                return false;
            }

            for (var index = 0; index < cachedNodes.Length; index++)
            {
                if (!ReferenceEquals(cachedNodes[index], resourceNodes[index]))
                {
                    return false;
                }
            }

            cachedResourceNodes = cachedNodes;
            return true;
        }
    }
}
