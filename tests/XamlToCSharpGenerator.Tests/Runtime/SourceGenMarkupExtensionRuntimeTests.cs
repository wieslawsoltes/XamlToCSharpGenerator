using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Media;
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
    public void ProvideStaticResource_Does_Not_Use_KeyNotFound_Control_Flow_For_Successful_Fallback_Resolution()
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

        var firstChanceCount = 0;

        void OnFirstChance(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is KeyNotFoundException &&
                args.Exception.StackTrace?.Contains(
                    "Avalonia.Markup.Xaml",
                    StringComparison.Ordinal) == true)
            {
                firstChanceCount++;
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        try
        {
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
            Assert.Equal(0, firstChanceCount);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        }
    }

    [Fact]
    public void ProvideStaticResource_Preserves_Color_To_Brush_Coercion_When_Fallback_Resolves_First()
    {
        var parentStack = new object[]
        {
            new Hashtable
            {
                ["AccentColor"] = Colors.Red
            }
        };

        var value = SourceGenMarkupExtensionRuntime.ProvideStaticResource(
            resourceKey: "AccentColor",
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: Border.BackgroundProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: parentStack);

        var brush = Assert.IsAssignableFrom<IBrush>(value);
        var solidColorBrush = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.Equal(Colors.Red, solidColorBrush.Color);
    }

    [Fact]
    public void ProvideStaticResource_Returns_UnsetValue_For_Control_Target_When_Resource_Is_Missing()
    {
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<BorderTarget, object?>(
            nameof(BorderTarget.Value),
            static (target, value) => target.Value = value);

        var value = SourceGenMarkupExtensionRuntime.ProvideStaticResource(
            resourceKey: "MissingResource",
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: new BorderTarget(),
            targetProperty: propertyInfo,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        Assert.Same(AvaloniaProperty.UnsetValue, value);
    }

    [Fact]
    public void ProvideStaticResource_Throws_When_Resource_Is_Missing_And_Target_Is_Not_Control()
    {
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<TargetHolder, object?>(
            nameof(TargetHolder.Value),
            static (target, value) => target.Value = value);

        Assert.Throws<KeyNotFoundException>(() =>
            SourceGenMarkupExtensionRuntime.ProvideStaticResource(
                resourceKey: "MissingResource",
                parentServiceProvider: null,
                rootObject: new object(),
                intermediateRootObject: new object(),
                targetObject: new TargetHolder(),
                targetProperty: propertyInfo,
                baseUri: "avares://Demo/MainView.axaml",
                parentStack: null));
    }

    [Fact]
    public void ProvideDynamicResource_Returns_DynamicResourceExtension_For_Anchored_Target()
    {
        var target = new Border
        {
            DataContext = new object()
        };

        var value = SourceGenMarkupExtensionRuntime.ProvideDynamicResource(
            resourceKey: "AccentBrush",
            parentServiceProvider: null,
            rootObject: new object(),
            intermediateRootObject: new object(),
            targetObject: target,
            targetProperty: null,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        Assert.IsAssignableFrom<IBinding>(value);
        Assert.IsType<DynamicResourceExtension>(value);
    }

    [Fact]
    public void ProvideDynamicResource_Returns_InstancedBinding_For_Detached_Styled_Target()
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

        Assert.IsType<InstancedBinding>(value);
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
    public void AttachBindingNameScope_Sets_NameScope_On_BindingBase()
    {
        var binding = new Binding("Text");
        var nameScope = new NameScope();

        var returned = SourceGenMarkupExtensionRuntime.AttachBindingNameScope(binding, nameScope);

        Assert.Same(binding, returned);
        Assert.NotNull(binding.NameScope);
        Assert.True(binding.NameScope!.TryGetTarget(out var resolvedNameScope));
        Assert.Same(nameScope, resolvedNameScope);
    }

    [Fact]
    public void AttachBindingNameScope_Configures_TypeResolver_For_Attached_Property_Paths()
    {
        var binding = new Binding("(ScrollViewer.IsScrollInertiaEnabled)")
        {
            ElementName = "PART_ContentPresenter"
        };

        SourceGenMarkupExtensionRuntime.AttachBindingNameScope(binding, new NameScope());

        Assert.NotNull(binding.TypeResolver);
        var resolvedType = binding.TypeResolver!(null, "ScrollViewer");
        Assert.Equal(typeof(ScrollViewer), resolvedType);
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
    public void ProvideOnFormFactor_Uses_OsFallback_When_Default_Is_Missing()
    {
        var expected = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
            ? "Mobile"
            : "Desktop";

        var value = SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(
            defaultValue: null,
            desktop: "Desktop",
            mobile: "Mobile",
            tv: "TV",
            parentServiceProvider: null);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void CoerceMarkupExtensionValue_Returns_Default_For_Null_ValueType()
    {
        var value = SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<BindingMode>(null);
        Assert.Equal(default, value);
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

    [Fact]
    public void ProvideMarkupExtension_Normalizes_Relative_BaseUri_To_Absolute_Avares_Uri()
    {
        var value = SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(
            extension: new BaseUriProbeMarkupExtension(),
            parentServiceProvider: null,
            rootObject: new TargetHolder(),
            intermediateRootObject: new object(),
            targetObject: new Border(),
            targetProperty: null,
            baseUri: "/Accents/BaseColorsPalette.xaml",
            parentStack: null);

        var resolvedBaseUri = Assert.IsType<string>(value);
        var parsedBaseUri = new Uri(resolvedBaseUri, UriKind.Absolute);
        var assemblyName = typeof(TargetHolder).Assembly.GetName().Name;
        Assert.Equal("avares", parsedBaseUri.Scheme);
        Assert.Equal(assemblyName, parsedBaseUri.Host, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("/Accents/BaseColorsPalette.xaml", parsedBaseUri.AbsolutePath);
    }

    [Fact]
    public void ProvideMarkupExtension_Supports_StaticResourceExtension_Fallback_Resolution()
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

        var value = SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(
            extension: new StaticResourceExtension("AccentBrush"),
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
    public void ApplyBinding_Does_Not_Throw_For_DataContext_Unavailable_InvalidOperation()
    {
        var root = new UserControl();
        var panel = new StackPanel();
        var anchor = new Border();
        var flyout = new MenuFlyout();
        var binding = new Binding("MenuItems");

        panel.Children.Add(anchor);
        root.Content = panel;
        anchor.ContextFlyout = flyout;

        var applyException = Record.Exception(() =>
            SourceGenMarkupExtensionRuntime.ApplyBinding(
                flyout,
                MenuFlyout.ItemsSourceProperty,
                binding,
                anchor));

        Assert.Null(applyException);

        var dataContextException = Record.Exception(() =>
            root.DataContext = new ContextPageViewModel());

        Assert.Null(dataContextException);
    }

    [Fact]
    public void ResolveBindingAnchor_Prefers_Detached_Styled_Parent_For_Detached_Target()
    {
        var contextMenu = new ContextMenu();
        var menuItem = new MenuItem();
        contextMenu.Items.Add(menuItem);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, [menuItem, contextMenu]);

        Assert.Same(contextMenu, anchor);
    }

    [Fact]
    public void ApplyBinding_Retries_When_Detached_Parent_Anchor_Gains_DataContext()
    {
        var contextMenu = new ContextMenu();
        var menuItem = new MenuItem();
        contextMenu.Items.Add(menuItem);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, [menuItem, contextMenu]);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            menuItem,
            HeaderedSelectingItemsControl.HeaderProperty,
            new Binding("Message"),
            anchor);

        contextMenu.DataContext = new DeferredAnchorViewModel();

        Assert.Equal("Close", menuItem.Header);
    }

    [Fact]
    public void ApplyExpressionBinding_Retries_When_Detached_Parent_Anchor_Gains_DataContext()
    {
        var contextMenu = new ContextMenu();
        var menuItem = new MenuItem();
        contextMenu.Items.Add(menuItem);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, [menuItem, contextMenu]);
        var binding = SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<DeferredAnchorViewModel>(
            static source => source.Message,
            Array.Empty<string>(),
            parentServiceProvider: null,
            rootObject: contextMenu,
            intermediateRootObject: contextMenu,
            targetObject: menuItem,
            targetProperty: HeaderedSelectingItemsControl.HeaderProperty,
            baseUri: "avares://Demo/Menu.axaml",
            parentStack: [menuItem, contextMenu]);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            menuItem,
            HeaderedSelectingItemsControl.HeaderProperty,
            binding,
            anchor);

        contextMenu.DataContext = new DeferredAnchorViewModel();

        Assert.Equal("Close", menuItem.Header);
    }

    [Fact]
    public void Deferred_Context_Menu_Resource_Resolves_Dynamic_Resources_And_Command_Bindings_When_Requested()
    {
        var host = new Border();
        var rootResources = new ResourceDictionary();
        var menuResources = new ResourceDictionary();
        var stringResources = new ResourceDictionary();
        host.Resources = rootResources;
        rootResources.MergedDictionaries.Add(menuResources);
        rootResources.MergedDictionaries.Add(stringResources);
        stringResources["CloseHeader"] = "Close";

        var buildCount = 0;
        SourceGenObjectGraphRuntimeHelpers.TryAddToDictionary(
            menuResources,
            "Menu",
            SourceGenDeferredContentRuntime.CreateShared(__deferredServiceProvider =>
            {
                buildCount++;
                var resourceParentStack = new object[] { menuResources, rootResources, host };
                var provider = SourceGenDeferredServiceProviderFactory.CreateDeferredResourceServiceProvider(
                    __deferredServiceProvider,
                    host,
                    host,
                    "avares://Demo/Menu.axaml",
                    resourceParentStack);
                var contextMenu = new ContextMenu();
                var menuItem = new MenuItem();
                contextMenu.Items.Add(menuItem);

                var menuItemParentStack = new object[] { menuItem, contextMenu, menuResources, rootResources, host };
                var bindingAnchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, menuItemParentStack);

                SourceGenMarkupExtensionRuntime.ApplyBinding(
                    menuItem,
                    HeaderedSelectingItemsControl.HeaderProperty,
                    SourceGenMarkupExtensionRuntime.ProvideDynamicResource(
                        "CloseHeader",
                        provider,
                        host,
                        host,
                        menuItem,
                        HeaderedSelectingItemsControl.HeaderProperty,
                        "avares://Demo/Menu.axaml",
                        menuItemParentStack),
                    bindingAnchor);

                var commandBinding = SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<DeferredCommandViewModel>(
                    static source => SourceGenMethodCommandRuntime.Create(
                        source.Owner.Factory,
                        static (target, parameter) => ((ITestFactory)target).CloseDockable(SourceGenMethodCommandRuntime.ConvertParameter<DeferredDockable>(parameter)),
                        null,
                        new[] { "Owner", "Owner.Factory" }),
                    new[] { "Owner", "Owner.Factory" },
                    provider,
                    host,
                    host,
                    menuItem,
                    MenuItem.CommandProperty,
                    "avares://Demo/Menu.axaml",
                    menuItemParentStack);
                SourceGenMarkupExtensionRuntime.ApplyBinding(
                    menuItem,
                    MenuItem.CommandProperty,
                    commandBinding,
                    bindingAnchor);

                var commandParameterBinding = SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<DeferredCommandViewModel>(
                    static source => source.Dockable,
                    new[] { "Dockable" },
                    provider,
                    host,
                    host,
                    menuItem,
                    MenuItem.CommandParameterProperty,
                    "avares://Demo/Menu.axaml",
                    menuItemParentStack);
                SourceGenMarkupExtensionRuntime.ApplyBinding(
                    menuItem,
                    MenuItem.CommandParameterProperty,
                    commandParameterBinding,
                    bindingAnchor);

                return contextMenu;
            }),
            "avares://Demo/Menu.axaml");

        Assert.Equal(0, buildCount);

        var contextMenu = Assert.IsType<ContextMenu>(menuResources["Menu"]);
        var menuItem = Assert.Single(contextMenu.Items.OfType<MenuItem>());
        Assert.Equal(1, buildCount);
        Assert.Equal("Close", menuItem.Header);
        Assert.Null(menuItem.Command);

        var secondLookup = Assert.IsType<ContextMenu>(menuResources["Menu"]);
        Assert.Same(contextMenu, secondLookup);
        Assert.Equal(1, buildCount);

        var viewModel = new DeferredCommandViewModel();
        contextMenu.DataContext = viewModel;

        var command = Assert.IsAssignableFrom<ICommand>(menuItem.Command);
        Assert.Same(viewModel.Dockable, menuItem.CommandParameter);

        command.Execute(menuItem.CommandParameter);

        Assert.Equal(1, viewModel.Factory.CloseDockableCallCount);
        Assert.Same(viewModel.Dockable, viewModel.Factory.LastDockable);
    }

    [Fact]
    public void Detached_Context_Menu_Local_Dynamic_Resources_Override_And_Fall_Back_To_Owning_Resources()
    {
        var host = new Border();
        host.Resources = new ResourceDictionary
        {
            ["CloseHeader"] = "Outer Close"
        };

        var contextMenu = new ContextMenu
        {
            Resources = new ResourceDictionary
            {
                ["CloseHeader"] = "Inner Close"
            }
        };
        var menuItem = new MenuItem();
        contextMenu.Items.Add(menuItem);

        var parentStack = new object[] { menuItem, contextMenu, host.Resources, host };
        var bindingAnchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, parentStack);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            menuItem,
            HeaderedSelectingItemsControl.HeaderProperty,
            SourceGenMarkupExtensionRuntime.ProvideDynamicResource(
                "CloseHeader",
                parentServiceProvider: null,
                rootObject: host,
                intermediateRootObject: host,
                targetObject: menuItem,
                targetProperty: HeaderedSelectingItemsControl.HeaderProperty,
                baseUri: "avares://Demo/Menu.axaml",
                parentStack: parentStack),
            bindingAnchor);

        Assert.Equal("Inner Close", menuItem.Header);

        contextMenu.Resources.Remove("CloseHeader");

        Assert.Equal("Outer Close", menuItem.Header);
    }

    [Fact]
    public void Deferred_Resource_Preserves_Creation_Time_Type_Resolver_For_Typed_Bindings()
    {
        var host = new Border();
        var resources = new ResourceDictionary();
        host.Resources = resources;
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IXamlTypeResolver)] = new DeferredTypeResolver()
        });

        SourceGenObjectGraphRuntimeHelpers.TryAddToDictionary(
            resources,
            "Menu",
            SourceGenDeferredContentRuntime.CreateShared(parentProvider, __deferredServiceProvider =>
            {
                var resourceParentStack = new object[] { resources, host };
                var provider = SourceGenDeferredServiceProviderFactory.CreateDeferredResourceServiceProvider(
                    __deferredServiceProvider,
                    host,
                    host,
                    "avares://Demo/Menu.axaml",
                    resourceParentStack);
                var contextMenu = new ContextMenu();
                var menuItem = new MenuItem();
                contextMenu.Items.Add(menuItem);

                var menuItemParentStack = new object[] { menuItem, contextMenu, resources, host };
                var bindingAnchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, menuItemParentStack);
                var typedBinding = new ReflectionBindingExtension("((demo:IDeferredDock)Owner).CanClose")
                    .ProvideValue(provider);

                SourceGenMarkupExtensionRuntime.ApplyBinding(
                    menuItem,
                    MenuItem.IsVisibleProperty,
                    typedBinding,
                    bindingAnchor);

                return contextMenu;
            }),
            "avares://Demo/Menu.axaml");

        var contextMenu = Assert.IsType<ContextMenu>(resources["Menu"]);
        var menuItem = Assert.Single(contextMenu.Items.OfType<MenuItem>());

        contextMenu.DataContext = new DeferredTypedOwnerViewModel();

        Assert.False(menuItem.IsVisible);
    }

    [Fact]
    public void AttachBindingNameScope_Maps_Document_Prefixes_For_Runtime_Type_Casts()
    {
        SourceGenKnownTypeRegistry.RegisterType(typeof(IDeferredDock));

        var contextMenu = new ContextMenu();
        var menuItem = new MenuItem();
        contextMenu.Items.Add(menuItem);

        var binding = SourceGenMarkupExtensionRuntime.AttachBindingNameScope(
            new Binding("((core:IDeferredDock)Owner).CanClose"),
            nameScope: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["core"] = "using:XamlToCSharpGenerator.Tests.Runtime"
            });
        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, [menuItem, contextMenu]);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            menuItem,
            MenuItem.IsVisibleProperty,
            binding,
            anchor);

        contextMenu.DataContext = new DeferredTypedOwnerViewModel();

        Assert.False(menuItem.IsVisible);
    }

    [Fact]
    public void Deferred_Resource_Flattens_Static_Resource_Alias_When_Cached()
    {
        var host = new Border();
        var resources = new ResourceDictionary();
        host.Resources = resources;
        resources["BaseBrush"] = Brushes.Red;

        var buildCount = 0;
        SourceGenObjectGraphRuntimeHelpers.TryAddToDictionary(
            resources,
            "AliasBrush",
            SourceGenDeferredContentRuntime.CreateShared(__deferredServiceProvider =>
            {
                buildCount++;
                var resourceParentStack = new object[] { resources, host };
                var provider = SourceGenDeferredServiceProviderFactory.CreateDeferredResourceServiceProvider(
                    __deferredServiceProvider,
                    host,
                    host,
                    "avares://Demo/Resources.axaml",
                    resourceParentStack);

                return SourceGenMarkupExtensionRuntime.ProvideStaticResource(
                    "BaseBrush",
                    provider,
                    host,
                    host,
                    resources,
                    targetProperty: null,
                    "avares://Demo/Resources.axaml",
                    resourceParentStack);
            }),
            "avares://Demo/Resources.axaml");

        Assert.Equal(0, buildCount);

        var aliasBrush = Assert.IsAssignableFrom<ISolidColorBrush>(resources["AliasBrush"]);
        var secondLookup = Assert.IsAssignableFrom<ISolidColorBrush>(resources["AliasBrush"]);

        Assert.Same(Brushes.Red, aliasBrush);
        Assert.Same(aliasBrush, secondLookup);
        Assert.Equal(1, buildCount);
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

    private sealed class BaseUriProbeMarkupExtension : MarkupExtension
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var uriContext = serviceProvider.GetService(typeof(IUriContext)) as IUriContext;
            return uriContext?.BaseUri?.ToString() ?? string.Empty;
        }
    }

    private sealed class ContextPageViewModel
    {
        public IReadOnlyList<string> MenuItems { get; } = ["One", "Two"];
    }

    private sealed class DeferredAnchorViewModel
    {
        public string Message { get; } = "Close";
    }

    private sealed class DeferredTypedOwnerViewModel
    {
        public object Owner { get; } = new DeferredDockOwner();
    }

    private sealed class DeferredDockable
    {
    }

    private interface IDeferredDock
    {
        bool CanClose { get; }
    }

    private sealed class DeferredDockOwner : IDeferredDock
    {
        public bool CanClose => false;
    }

    private sealed class DeferredTypeResolver : IXamlTypeResolver
    {
        public Type Resolve(string qualifiedTypeName)
        {
            return qualifiedTypeName switch
            {
                "demo:IDeferredDock" => typeof(IDeferredDock),
                _ => throw new InvalidOperationException("Unexpected type: " + qualifiedTypeName)
            };
        }
    }

    private interface ITestFactory
    {
        void CloseDockable(DeferredDockable dockable);
    }

    private sealed class TestFactory : ITestFactory
    {
        public int CloseDockableCallCount { get; private set; }

        public DeferredDockable? LastDockable { get; private set; }

        public void CloseDockable(DeferredDockable dockable)
        {
            CloseDockableCallCount++;
            LastDockable = dockable;
        }
    }

    private sealed class DeferredCommandOwner
    {
        public DeferredCommandOwner(TestFactory factory)
        {
            Factory = factory;
        }

        public TestFactory Factory { get; }
    }

    private sealed class DeferredCommandViewModel
    {
        public DeferredCommandViewModel()
        {
            Factory = new TestFactory();
            Owner = new DeferredCommandOwner(Factory);
            Dockable = new DeferredDockable();
        }

        public DeferredCommandOwner Owner { get; }

        public TestFactory Factory { get; }

        public DeferredDockable Dockable { get; }
    }

}
