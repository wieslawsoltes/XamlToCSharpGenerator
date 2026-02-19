using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Utilities;

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
