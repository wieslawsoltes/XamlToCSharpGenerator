using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenDeferredServiceProviderFactoryTests
{
    [Fact]
    public void CreateTemplateNameScope_Creates_Local_Scope()
    {
        var parentNameScope = new NameScope();
        var parentValue = new object();
        parentNameScope.Register("FromParent", parentValue);

        var provider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(INameScope)] = parentNameScope
        });

        var templateScope = SourceGenDeferredServiceProviderFactory.CreateTemplateNameScope(provider);
        var localValue = new object();
        templateScope.Register("Local", localValue);

        Assert.Same(localValue, templateScope.Find("Local"));
        Assert.Null(templateScope.Find("FromParent"));
    }

    [Fact]
    public void CreateDeferredTemplateServiceProvider_Exposes_Template_Services_And_Forwards_Unknown_Services()
    {
        var parentSentinel = new ParentSentinel();
        var parentStack = new TestLazyParentStackProvider(Array.Empty<object>());
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(ParentSentinel)] = parentSentinel,
            [typeof(IAvaloniaXamlIlParentStackProvider)] = parentStack
        });
        var rootObject = new object();
        var templateScope = new NameScope();

        var provider = SourceGenDeferredServiceProviderFactory.CreateDeferredTemplateServiceProvider(
            parentProvider,
            rootObject,
            templateScope);

        Assert.Same(templateScope, provider.GetService(typeof(INameScope)));

        var rootProvider = Assert.IsAssignableFrom<IRootObjectProvider>(provider.GetService(typeof(IRootObjectProvider)));
        Assert.Same(rootObject, rootProvider.RootObject);
        Assert.Same(rootObject, rootProvider.IntermediateRootObject);

        Assert.NotNull(provider.GetService(typeof(IAvaloniaXamlIlControlTemplateProvider)));
        Assert.Same(parentSentinel, provider.GetService(typeof(ParentSentinel)));

        var providerParentStack = Assert.IsAssignableFrom<IAvaloniaXamlIlParentStackProvider>(
            provider.GetService(typeof(IAvaloniaXamlIlParentStackProvider)));
        Assert.Empty(providerParentStack.Parents);
    }

    [AvaloniaFact]
    public void CreateDeferredTemplateServiceProvider_Uses_Filtered_Resource_Parent_Stack_And_Appends_Upstream_Parents()
    {
        var resourceA = new ResourceDictionary
        {
            ["A"] = "A"
        };
        var resourceB = new ResourceDictionary
        {
            ["B"] = "B"
        };
        var parentStack = new TestLazyParentStackProvider(new object[]
        {
            "NotAResource",
            resourceA,
            resourceB
        });
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAvaloniaXamlIlParentStackProvider)] = parentStack
        });

        var provider = SourceGenDeferredServiceProviderFactory.CreateDeferredTemplateServiceProvider(
            parentProvider,
            new object(),
            new NameScope());

        var providerParentStack = Assert.IsAssignableFrom<IAvaloniaXamlIlParentStackProvider>(
            provider.GetService(typeof(IAvaloniaXamlIlParentStackProvider)));
        Assert.Equal(new object[] { resourceA, resourceB, "NotAResource" }, providerParentStack.Parents);
    }

    [AvaloniaFact]
    public void CreateDeferredResourceServiceProvider_Uses_Explicit_Parent_Resources_And_BaseUri()
    {
        var owner = new Border();
        var resources = new ResourceDictionary();
        owner.Resources = resources;

        var provider = SourceGenDeferredServiceProviderFactory.CreateDeferredResourceServiceProvider(
            parentServiceProvider: null,
            rootObject: owner,
            intermediateRootObject: resources,
            baseUri: "avares://Demo/Resources.axaml",
            parentStack: new object[] { resources, owner, "Ignored" });

        var rootProvider = Assert.IsAssignableFrom<IRootObjectProvider>(provider.GetService(typeof(IRootObjectProvider)));
        Assert.Same(owner, rootProvider.RootObject);
        Assert.Same(resources, rootProvider.IntermediateRootObject);

        var uriContext = Assert.IsAssignableFrom<IUriContext>(provider.GetService(typeof(IUriContext)));
        Assert.Equal("avares://Demo/Resources.axaml", uriContext.BaseUri.ToString());

        var parentStackProvider = Assert.IsAssignableFrom<IAvaloniaXamlIlParentStackProvider>(
            provider.GetService(typeof(IAvaloniaXamlIlParentStackProvider)));
        Assert.Equal(new object[] { resources, owner }, parentStackProvider.Parents);
    }

    [Fact]
    public void CreateDeferredResourceNameScope_Creates_Local_Scope()
    {
        var parentScope = new NameScope();
        var parentValue = new object();
        parentScope.Register("Parent", parentValue);

        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(INameScope)] = parentScope
        });

        var deferredScope = SourceGenDeferredServiceProviderFactory.CreateDeferredResourceNameScope(parentProvider);
        var localValue = new object();
        deferredScope.Register("Local", localValue);

        Assert.Same(localValue, deferredScope.Find("Local"));
        Assert.Null(deferredScope.Find("Parent"));

        deferredScope.Complete();

        Assert.True(deferredScope.IsCompleted);
    }

    private sealed class ParentSentinel
    {
    }

    private sealed class DictionaryServiceProvider : IServiceProvider
    {
        private readonly IReadOnlyDictionary<Type, object> _services;

        public DictionaryServiceProvider(IReadOnlyDictionary<Type, object> services)
        {
            _services = services;
        }

        public object? GetService(Type serviceType)
        {
            return _services.TryGetValue(serviceType, out var service) ? service : null;
        }
    }

    private sealed class TestLazyParentStackProvider : IAvaloniaXamlIlParentStackProvider
    {
        private readonly object[] _parents;

        public TestLazyParentStackProvider(object[] parents)
        {
            _parents = parents;
        }

        public IEnumerable<object> Parents => _parents;
    }
}
