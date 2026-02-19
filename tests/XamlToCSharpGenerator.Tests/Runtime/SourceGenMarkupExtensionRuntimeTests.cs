using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class SourceGenMarkupExtensionRuntimeTests
{
    [Fact]
    public void ProvideReference_Resolves_Value_From_NameScope()
    {
        var nameScope = new NameScope();
        var target = new object();
        nameScope.Register("Target", target);

        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(INameScope)] = nameScope
        });

        var value = SourceGenMarkupExtensionRuntime.ProvideReference(
            name: "Target",
            parentServiceProvider: parentProvider,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: null,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        Assert.Same(target, value);
    }

    [Fact]
    public void ProvideStaticResource_Resolves_From_ParentStack_ResourceNode()
    {
        var resourceNode = new ResourceDictionary
        {
            ["AccentBrush"] = "Orange"
        };
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAvaloniaXamlIlParentStackProvider)] = new TestParentStackProvider([resourceNode])
        });

        var value = SourceGenMarkupExtensionRuntime.ProvideStaticResource(
            resourceKey: "AccentBrush",
            parentServiceProvider: parentProvider,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: null,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        Assert.Equal("Orange", value);
    }

    [Fact]
    public void ProvideStaticResource_Falls_Back_To_Explicit_ParentStack_When_Extension_Returns_UnsetValue()
    {
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<BorderTarget, object?>(
            nameof(BorderTarget.Value),
            static (target, value) => target.Value = value);
        var parentStack = new object[]
        {
            new Hashtable
            {
                ["AccentBrush"] = "Purple"
            }
        };

        var value = SourceGenMarkupExtensionRuntime.ProvideStaticResource(
            resourceKey: "AccentBrush",
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new BorderTarget(),
            targetProperty: propertyInfo,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: parentStack);

        Assert.Equal("Purple", value);
    }

    [Fact]
    public void ProvideDynamicResource_Returns_IBinding()
    {
        var value = SourceGenMarkupExtensionRuntime.ProvideDynamicResource(
            resourceKey: "AccentBrush",
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: null,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        Assert.IsAssignableFrom<IBinding>(value);
        Assert.IsType<DynamicResourceExtension>(value);
    }

    [Fact]
    public void ProvideReference_Uses_ProvideValueTarget_Property_For_Deferred_Name_Resolution()
    {
        var nameScope = new NameScope();
        var target = new TargetHolder();
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<TargetHolder, object?>(
            nameof(TargetHolder.Value),
            static (holder, value) => holder.Value = value);
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(INameScope)] = nameScope
        });

        var result = SourceGenMarkupExtensionRuntime.ProvideReference(
            name: "DeferredTarget",
            parentServiceProvider: parentProvider,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: target,
            targetProperty: propertyInfo,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        Assert.Null(result);
        Assert.Null(target.Value);

        var deferredValue = new object();
        nameScope.Register("DeferredTarget", deferredValue);

        Assert.Same(deferredValue, target.Value);
    }

    [Fact]
    public void ProvideReflectionBinding_Returns_Binding_With_Path()
    {
        var extension = new ReflectionBindingExtension("Message");

        var value = SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(
            extension,
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: null,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        var binding = Assert.IsType<Binding>(value);
        Assert.Equal("Message", binding.Path);
    }

    [Fact]
    public void ProvideOnPlatform_Selects_Current_Platform_Option()
    {
        var expected = OperatingSystem.IsWindows() ? "Windows" :
            OperatingSystem.IsMacOS() ? "macOS" :
            OperatingSystem.IsLinux() ? "Linux" :
            OperatingSystem.IsAndroid() ? "Android" :
            OperatingSystem.IsIOS() ? "iOS" :
            OperatingSystem.IsBrowser() ? "Browser" :
            "Default";

        var value = SourceGenMarkupExtensionRuntime.ProvideOnPlatform(
            defaultValue: "Default",
            windows: "Windows",
            macOs: "macOS",
            linux: "Linux",
            android: "Android",
            ios: "iOS",
            browser: "Browser");

        Assert.Equal(expected, value);
    }

    [Fact]
    public void ProvideOnFormFactor_Falls_Back_To_Default_Without_RuntimePlatform_Service()
    {
        var value = SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(
            defaultValue: "Default",
            desktop: "Desktop",
            mobile: "Mobile",
            tv: "TV",
            parentServiceProvider: null);

        Assert.Equal("Default", value);
    }

    [Fact]
    public void ProvideMarkupExtension_Exposes_ProvideValue_Context_Contracts()
    {
        var value = SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(
            extension: new ProbeMarkupExtension(),
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: null,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: [new object()]);

        Assert.Equal("True|True|True|avares|True", value);
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

    private sealed class TestParentStackProvider : IAvaloniaXamlIlParentStackProvider
    {
        private readonly object[] _parents;

        public TestParentStackProvider(object[] parents)
        {
            _parents = parents;
        }

        public IEnumerable<object> Parents => _parents;
    }

    private sealed class TargetHolder
    {
        public object? Value { get; set; }
    }

    private sealed class BorderTarget : Border
    {
        public object? Value { get; set; }
    }

    private sealed class ProbeMarkupExtension : MarkupExtension
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var provideValueTarget = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
            var rootProvider = serviceProvider.GetService(typeof(IRootObjectProvider)) as IRootObjectProvider;
            var uriContext = serviceProvider.GetService(typeof(IUriContext)) as IUriContext;
            var parentStackProvider = serviceProvider.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) as IAvaloniaXamlIlParentStackProvider;

            var hasProvideValueTarget = provideValueTarget?.TargetObject is not null;
            var hasTargetProperty = provideValueTarget?.TargetProperty is not null;
            var hasRootObject = rootProvider?.RootObject is not null;
            var scheme = uriContext?.BaseUri?.Scheme ?? "<none>";
            var hasParentStack = parentStackProvider?.Parents.Any() == true;

            return $"{hasProvideValueTarget}|{hasTargetProperty}|{hasRootObject}|{scheme}|{hasParentStack}";
        }
    }

}
