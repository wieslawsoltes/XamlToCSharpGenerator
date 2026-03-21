using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Data.Core;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;
using Avalonia.Threading;
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void ProvideDynamicResource_For_Detached_NonStyled_Target_Uses_Upstream_Resource_Parents()
    {
        var upstreamResources = new ResourceDictionary
        {
            ["AccentColor"] = Colors.Red
        };
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAvaloniaXamlIlParentStackProvider)] = new TestParentStackProvider([upstreamResources])
        });
        var localThemeDictionary = new ResourceDictionary();
        var brush = new SolidColorBrush();

        var value = SourceGenMarkupExtensionRuntime.ProvideDynamicResource(
            resourceKey: "AccentColor",
            parentServiceProvider: parentProvider,
            rootObject: upstreamResources,
            intermediateRootObject: upstreamResources,
            targetObject: brush,
            targetProperty: SolidColorBrush.ColorProperty,
            baseUri: "avares://Demo/Theme.axaml",
            parentStack: [localThemeDictionary]);

        var binding = Assert.IsType<InstancedBinding>(value);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            brush,
            SolidColorBrush.ColorProperty,
            binding);

        Assert.Equal(Colors.Red, brush.Color);
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void ApplyExpressionBinding_Retries_When_KeyBinding_Anchor_Gains_DataContext()
    {
        var root = new UserControl();
        var keyBinding = new global::Avalonia.Input.KeyBinding();
        root.KeyBindings.Add(keyBinding);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(keyBinding, [keyBinding, root]);
        var commandBinding = SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<DeferredKeyBindingViewModel>(
            static source => source.Trigger,
            new[] { "Trigger" },
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: keyBinding,
            targetProperty: global::Avalonia.Input.KeyBinding.CommandProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: [keyBinding, root]);
        var parameterBinding = SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<DeferredKeyBindingViewModel>(
            static source => source,
            Array.Empty<string>(),
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: keyBinding,
            targetProperty: global::Avalonia.Input.KeyBinding.CommandParameterProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: [keyBinding, root]);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            keyBinding,
            global::Avalonia.Input.KeyBinding.CommandProperty,
            commandBinding,
            anchor);
        SourceGenMarkupExtensionRuntime.ApplyBinding(
            keyBinding,
            global::Avalonia.Input.KeyBinding.CommandParameterProperty,
            parameterBinding,
            anchor);

        Assert.Null(keyBinding.Command);
        Assert.Null(keyBinding.CommandParameter);

        var viewModel = new DeferredKeyBindingViewModel();
        root.DataContext = viewModel;
        Dispatcher.UIThread.RunJobs();

        var command = Assert.IsAssignableFrom<ICommand>(keyBinding.Command);
        Assert.Same(viewModel, keyBinding.CommandParameter);

        command.Execute(keyBinding.CommandParameter);

        Assert.Equal(1, viewModel.ExecuteCount);
        Assert.Same(viewModel, viewModel.LastParameter);
    }

    [AvaloniaFact]
    public void ApplyBinding_Retries_When_KeyBinding_Anchor_Gains_DataContext()
    {
        var root = new UserControl();
        var keyBinding = new global::Avalonia.Input.KeyBinding();
        root.KeyBindings.Add(keyBinding);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(keyBinding, [keyBinding, root]);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            keyBinding,
            global::Avalonia.Input.KeyBinding.CommandProperty,
            new Binding("Trigger"),
            anchor);
        SourceGenMarkupExtensionRuntime.ApplyBinding(
            keyBinding,
            global::Avalonia.Input.KeyBinding.CommandParameterProperty,
            new Binding("."),
            anchor);
        keyBinding.Gesture = new global::Avalonia.Input.KeyGesture(
            global::Avalonia.Input.Key.N,
            global::Avalonia.Input.KeyModifiers.Control);

        Assert.Null(keyBinding.Command);
        Assert.Null(keyBinding.CommandParameter);

        var viewModel = new DeferredKeyBindingViewModel();
        root.DataContext = viewModel;
        Dispatcher.UIThread.RunJobs();

        var command = Assert.IsAssignableFrom<ICommand>(keyBinding.Command);
        Assert.Same(viewModel, keyBinding.CommandParameter);

        command.Execute(keyBinding.CommandParameter);

        Assert.Equal(1, viewModel.ExecuteCount);
        Assert.Same(viewModel, viewModel.LastParameter);
    }

    [AvaloniaFact]
    public void ApplyBinding_Resolves_ElementName_For_KeyBinding_CommandParameter()
    {
        var root = new UserControl();
        var nameScope = new NameScope();
        var source = new TextBlock
        {
            Text = "Close"
        };
        nameScope.Register("ShortcutTarget", source);

        var keyBinding = new global::Avalonia.Input.KeyBinding();
        root.KeyBindings.Add(keyBinding);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(keyBinding, [keyBinding, root]);
        var binding = SourceGenMarkupExtensionRuntime.AttachBindingNameScope(
            new Binding("Text")
            {
                ElementName = "ShortcutTarget"
            },
            nameScope);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            keyBinding,
            global::Avalonia.Input.KeyBinding.CommandParameterProperty,
            binding,
            anchor);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Close", keyBinding.CommandParameter);

        source.Text = "Open";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Open", keyBinding.CommandParameter);
    }

    [AvaloniaFact]
    public void ApplyBinding_Preserves_Rooted_ElementName_Path_For_KeyBinding_CommandParameter()
    {
        var root = new UserControl();
        var nameScope = new NameScope();
        var source = new TextBlock
        {
            Text = "Close"
        };
        nameScope.Register("ShortcutTarget", source);
        var keyBinding = new global::Avalonia.Input.KeyBinding();
        root.KeyBindings.Add(keyBinding);

        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(keyBinding, [keyBinding, root]);
        var binding = SourceGenMarkupExtensionRuntime.AttachBindingNameScope(
            new Binding("#ShortcutTarget.Text"),
            nameScope);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            keyBinding,
            global::Avalonia.Input.KeyBinding.CommandParameterProperty,
            binding,
            anchor);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Close", keyBinding.CommandParameter);
    }

    [AvaloniaFact]
    public void ApplyBinding_Retries_FindAncestor_When_Detached_Anchor_Attaches_To_Tree()
    {
        var root = new UserControl
        {
            DataContext = new DeferredAnchorViewModel()
        };
        var panel = new StackPanel();
        root.Content = panel;

        var anchor = new Border();
        var target = new DeferredAncestorBindingTarget();
        var binding = new Binding("DataContext.Message")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
            {
                AncestorType = typeof(UserControl),
                AncestorLevel = 1
            }
        };

        var applyException = Record.Exception(() =>
            SourceGenMarkupExtensionRuntime.ApplyBinding(
                target,
                DeferredAncestorBindingTarget.ValueProperty,
                binding,
                anchor));

        Assert.Null(applyException);
        Assert.Null(target.Value);

        var window = new Window
        {
            Content = root
        };

        try
        {
            panel.Children.Add(anchor);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Close", target.Value);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
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
    public void AttachBindingNameScope_Maps_Document_Prefixes_For_Nested_MultiBinding_Runtime_Type_Casts()
    {
        SourceGenKnownTypeRegistry.RegisterType(typeof(IDeferredDock));

        var contextMenu = new ContextMenu();
        var menuItem = new MenuItem();
        contextMenu.Items.Add(menuItem);

        var multiBinding = SourceGenMarkupExtensionRuntime.AttachBindingNameScope(
            new MultiBinding
            {
                Converter = new AllTrueMultiValueConverter(),
                Bindings =
                {
                    new MultiBinding
                    {
                        Converter = new AllTrueMultiValueConverter(),
                        Bindings =
                        {
                            new Binding("((core:IDeferredDock)Owner).CanClose"),
                            new Binding("((core:IDeferredDock)Owner).CanClose")
                        }
                    }
                }
            },
            nameScope: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["core"] = "using:XamlToCSharpGenerator.Tests.Runtime"
            });
        var anchor = SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(menuItem, [menuItem, contextMenu]);

        SourceGenMarkupExtensionRuntime.ApplyBinding(
            menuItem,
            MenuItem.IsVisibleProperty,
            multiBinding,
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

    [Fact]
    public void Deferred_Resource_Lookup_Returns_Fresh_Instance_When_XShared_Is_False()
    {
        var resources = new ResourceDictionary();
        var buildCount = 0;

        SourceGenObjectGraphRuntimeHelpers.TryAddToDictionary(
            resources,
            "Overlay",
            SourceGenDeferredContentRuntime.CreateShared(__ =>
            {
                buildCount++;
                return new Border();
            }),
            "avares://Demo/Resources.axaml",
            isShared: false);

        Assert.Equal(0, buildCount);

        var firstLookup = Assert.IsType<Border>(resources["Overlay"]);
        var secondLookup = Assert.IsType<Border>(resources["Overlay"]);

        Assert.NotSame(firstLookup, secondLookup);
        Assert.Equal(2, buildCount);
    }

    [Fact]
    public void CreateObjectConstructionServiceProvider_Exposes_RootObject_UriContext_And_ParentStack()
    {
        var upstreamParent = new Border();
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAvaloniaXamlIlParentStackProvider)] = new TestParentStackProvider([upstreamParent])
        });
        var root = new UserControl();
        var intermediateRoot = new Border();

        var provider = SourceGenMarkupExtensionRuntime.CreateObjectConstructionServiceProvider(
            parentProvider,
            root,
            intermediateRoot,
            "avares://Demo/App.axaml",
            [root]);

        var rootProvider = Assert.IsAssignableFrom<IRootObjectProvider>(provider.GetService(typeof(IRootObjectProvider)));
        Assert.Same(root, rootProvider.RootObject);
        Assert.Same(intermediateRoot, rootProvider.IntermediateRootObject);

        var uriContext = Assert.IsAssignableFrom<IUriContext>(provider.GetService(typeof(IUriContext)));
        Assert.Equal(new Uri("avares://Demo/App.axaml"), uriContext.BaseUri);

        var parentStackProvider = Assert.IsAssignableFrom<IAvaloniaXamlIlParentStackProvider>(
            provider.GetService(typeof(IAvaloniaXamlIlParentStackProvider)));
        Assert.Equal([root, upstreamParent], parentStackProvider.Parents.ToArray());
    }

    [Fact]
    public void CreateTypeConverterContext_Exposes_TypeDescriptorContext_And_Markup_Services()
    {
        var upstreamParent = new Border();
        var parentProvider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAvaloniaXamlIlParentStackProvider)] = new TestParentStackProvider([upstreamParent])
        });
        var root = new UserControl();
        var intermediateRoot = new Border();
        var target = new BorderTarget();
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<BorderTarget, object?>(
            nameof(BorderTarget.Value),
            static (holder, value) => holder.Value = value);

        var context = SourceGenMarkupExtensionRuntime.CreateTypeConverterContext(
            parentProvider,
            root,
            intermediateRoot,
            target,
            propertyInfo,
            "avares://Demo/App.axaml",
            [target]);

        Assert.Same(context, context.GetService(typeof(ITypeDescriptorContext)));
        Assert.Same(target, context.Instance);
        Assert.Null(context.Container);
        Assert.NotNull(context.PropertyDescriptor);
        var propertyDescriptor = context.PropertyDescriptor!;
        Assert.Equal(nameof(BorderTarget.Value), propertyDescriptor.Name);
        Assert.Equal(typeof(BorderTarget), propertyDescriptor.ComponentType);
        Assert.Equal(typeof(object), propertyDescriptor.PropertyType);
        Assert.False(propertyDescriptor.IsReadOnly);
        Assert.Throws<NotSupportedException>(context.OnComponentChanged);
        Assert.Throws<NotSupportedException>(() => context.OnComponentChanging());

        var rootProvider = Assert.IsAssignableFrom<IRootObjectProvider>(context.GetService(typeof(IRootObjectProvider)));
        Assert.Same(root, rootProvider.RootObject);
        Assert.Same(intermediateRoot, rootProvider.IntermediateRootObject);

        var provideValueTarget = Assert.IsAssignableFrom<IProvideValueTarget>(context.GetService(typeof(IProvideValueTarget)));
        Assert.Same(target, provideValueTarget.TargetObject);
        Assert.Same(propertyInfo, provideValueTarget.TargetProperty);

        var uriContext = Assert.IsAssignableFrom<IUriContext>(context.GetService(typeof(IUriContext)));
        Assert.Equal(new Uri("avares://Demo/App.axaml"), uriContext.BaseUri);

        var parentStackProvider = Assert.IsAssignableFrom<IAvaloniaXamlIlParentStackProvider>(
            context.GetService(typeof(IAvaloniaXamlIlParentStackProvider)));
        Assert.Equal([target, upstreamParent], parentStackProvider.Parents.ToArray());
    }

    [Fact]
    public void CreateTypeConverterContext_Allows_Missing_Root_Anchors_For_Root_Factory_Conversion()
    {
        var context = SourceGenMarkupExtensionRuntime.CreateTypeConverterContext(
            parentServiceProvider: null,
            rootObject: null,
            intermediateRootObject: null,
            targetObject: null,
            targetProperty: null,
            baseUri: "avares://Demo/App.axaml",
            parentStack: null);

        var rootProvider = Assert.IsAssignableFrom<IRootObjectProvider>(context.GetService(typeof(IRootObjectProvider)));
        Assert.Null(rootProvider.RootObject);
        Assert.Null(rootProvider.IntermediateRootObject);

        var provideValueTarget = Assert.IsAssignableFrom<IProvideValueTarget>(context.GetService(typeof(IProvideValueTarget)));
        Assert.Null(provideValueTarget.TargetObject);
        Assert.Same(AvaloniaProperty.UnsetValue, provideValueTarget.TargetProperty);

        var uriContext = Assert.IsAssignableFrom<IUriContext>(context.GetService(typeof(IUriContext)));
        Assert.Equal(new Uri("avares://Demo/App.axaml"), uriContext.BaseUri);

        var parentStackProvider = Assert.IsAssignableFrom<IAvaloniaXamlIlParentStackProvider>(
            context.GetService(typeof(IAvaloniaXamlIlParentStackProvider)));
        Assert.Empty(parentStackProvider.Parents);
    }

    [AvaloniaFact]
    public void ProvideXBindExpressionBinding_TwoWay_Root_Assignment_Does_Not_Reenter_BindBack()
    {
        var root = new XBindTwoWayLoopProbe("Catalog alias");
        var target = new TextBox();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindTwoWayLoopProbe, XBindTwoWayLoopProbe, TextBox>(
            static (source, _, _) => source.Alias,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindTwoWayLoopProbe.Alias), null)
            ],
            mode: BindingMode.TwoWay,
            bindBack: static (source, value) => source.Alias = SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<string>(value),
            bindBackValueType: typeof(string),
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Default,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBox.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBox.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Catalog alias", target.Text);
        Assert.InRange(root.AliasSetCount, 0, 1);

        root.ResetAliasSetCount();
        target.Text = "Updated alias";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Updated alias", root.Alias);
        Assert.Equal(1, root.AliasSetCount);
    }

    [AvaloniaFact]
    public void ProvideXBindExpressionBinding_TwoWay_Explicit_BindBack_Normalization_Does_Not_Reenter()
    {
        var root = new XBindNormalizingLoopProbe(" initial search ");
        var target = new TextBox();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindNormalizingLoopProbe, XBindNormalizingLoopProbe, TextBox>(
            static (source, _, _) => source.SearchDraft,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindNormalizingLoopProbe.SearchDraft), null)
            ],
            mode: BindingMode.TwoWay,
            bindBack: static (source, value) => source.ApplySearchDraft(SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<string>(value)),
            bindBackValueType: typeof(string),
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Default,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBox.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBox.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        root.ResetBindBackCount();
        target.Text = "  Updated search  ";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Updated search", root.SearchDraft);
        Assert.Equal("Updated search", target.Text);
        Assert.Equal(1, root.BindBackCount);
    }

    [AvaloniaFact]
    public void ProvideXBindExpressionBinding_Resolves_Named_Elements_From_Ancestor_NameScopes()
    {
        var nameScope = new NameScope();
        var host = new StackPanel();
        NameScope.SetNameScope(host, nameScope);

        var editor = new TextBox();
        var target = new TextBlock();
        host.Children.Add(editor);
        host.Children.Add(target);
        nameScope.Register("Editor", editor);

        var resolved = SourceGenMarkupExtensionRuntime.ResolveNamedElement<TextBox>(target, new object(), "Editor");

        Assert.Same(editor, resolved);
    }

    [AvaloniaFact]
    public void ApplyBinding_XBind_ElementNameBinding_Preserves_Attached_NameScope_Metadata()
    {
        var nameScope = new NameScope();
        var host = new StackPanel();
        NameScope.SetNameScope(host, nameScope);

        var editor = new TextBox
        {
            Text = "Named scope text"
        };
        var target = new TextBlock();

        host.Children.Add(editor);
        host.Children.Add(target);
        nameScope.Register("Editor", editor);

        var binding = SourceGenMarkupExtensionRuntime.AttachBindingNameScope(
            SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<StackPanel, StackPanel, TextBlock>(
                static (_, root, targetObject) => SourceGenMarkupExtensionRuntime.ResolveNamedElement<TextBox>(targetObject, root, "Editor")?.Text,
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
                dependencies:
                [
                    new SourceGenBindingDependency(SourceGenBindingSourceKind.ElementName, ".", "Editor"),
                    new SourceGenBindingDependency(SourceGenBindingSourceKind.ElementName, "Text", "Editor")
                ],
                mode: BindingMode.OneWay,
                bindBack: null,
                bindBackValueType: null,
                converter: null,
                converterCulture: null,
                converterParameter: null,
                stringFormat: null,
                fallbackValue: null,
                targetNullValue: null,
                delay: 0,
                updateSourceTrigger: UpdateSourceTrigger.Default,
                priority: BindingPriority.LocalValue,
                parentServiceProvider: null,
                rootObject: host,
                intermediateRootObject: host,
                targetObject: target,
                targetProperty: TextBlock.TextProperty,
                baseUri: "avares://Demo/MainView.axaml",
                parentStack: [target, host]),
            nameScope);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBlock.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Named scope text", target.Text);
    }

    [AvaloniaFact]
    public void ProvideXBindExpressionBinding_Converter_Sees_Raw_Evaluator_Result_Before_Target_Coercion()
    {
        var root = new XBindConverterProbe(41);
        var target = new TextBlock();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindConverterProbe, XBindConverterProbe, TextBlock>(
            static (source, _, _) => source.Count,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindConverterProbe.Count), null)
            ],
            mode: BindingMode.OneWay,
            bindBack: null,
            bindBackValueType: null,
            converter: new XBindRawValueAwareConverter(),
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Default,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBlock.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBlock.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("42", target.Text);
    }

    [AvaloniaFact]
    public void ProvideXBindExpressionBinding_TwoWay_BindBack_Honors_Delay()
    {
        var root = new XBindNormalizingLoopProbe(" initial search ");
        var target = new TextBox();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindNormalizingLoopProbe, XBindNormalizingLoopProbe, TextBox>(
            static (source, _, _) => source.SearchDraft,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindNormalizingLoopProbe.SearchDraft), null)
            ],
            mode: BindingMode.TwoWay,
            bindBack: static (source, value) => source.ApplySearchDraft(SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<string>(value)),
            bindBackValueType: typeof(string),
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 40,
            updateSourceTrigger: UpdateSourceTrigger.PropertyChanged,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBox.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBox.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        root.ResetBindBackCount();
        target.Text = "  Updated search  ";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(" initial search ", root.SearchDraft);
        Assert.Equal(0, root.BindBackCount);

        WaitForDispatcherCondition(() => root.BindBackCount == 1, TimeSpan.FromSeconds(1));

        Assert.Equal("Updated search", root.SearchDraft);
        Assert.Equal("Updated search", target.Text);
        Assert.Equal(1, root.BindBackCount);
    }

    [AvaloniaFact]
    public void ProvideXBindExpressionBinding_TwoWay_BindBack_Honors_LostFocus_Trigger()
    {
        var root = new XBindNormalizingLoopProbe(" initial search ");
        var target = new TextBox();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindNormalizingLoopProbe, XBindNormalizingLoopProbe, TextBox>(
            static (source, _, _) => source.SearchDraft,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindNormalizingLoopProbe.SearchDraft), null)
            ],
            mode: BindingMode.TwoWay,
            bindBack: static (source, value) => source.ApplySearchDraft(SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<string>(value)),
            bindBackValueType: typeof(string),
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 250,
            updateSourceTrigger: UpdateSourceTrigger.LostFocus,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBox.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBox.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        root.ResetBindBackCount();
        target.Text = "  Lost focus value  ";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(" initial search ", root.SearchDraft);
        Assert.Equal(0, root.BindBackCount);

        target.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent)
        {
            Source = target
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Lost focus value", root.SearchDraft);
        Assert.Equal("Lost focus value", target.Text);
        Assert.Equal(1, root.BindBackCount);
    }

    [AvaloniaFact]
    public void UpdateXBind_Flushes_Explicit_BindBack_Pending_Value()
    {
        var root = new XBindNormalizingLoopProbe(" initial search ");
        var target = new TextBox();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindNormalizingLoopProbe, XBindNormalizingLoopProbe, TextBox>(
            static (source, _, _) => source.SearchDraft,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindNormalizingLoopProbe.SearchDraft), null)
            ],
            mode: BindingMode.TwoWay,
            bindBack: static (source, value) => source.ApplySearchDraft(SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<string>(value)),
            bindBackValueType: typeof(string),
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Explicit,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBox.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBox.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();

        root.ResetBindBackCount();
        target.Text = "  Explicit update  ";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(" initial search ", root.SearchDraft);
        Assert.Equal(0, root.BindBackCount);

        SourceGenMarkupExtensionRuntime.UpdateXBind(root);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Explicit update", root.SearchDraft);
        Assert.Equal("Explicit update", target.Text);
        Assert.Equal(1, root.BindBackCount);
    }

    [AvaloniaFact]
    public void StopTrackingXBind_Can_Be_Followed_By_InitializeXBind()
    {
        var root = new XBindTwoWayLoopProbe("Catalog alias");
        var target = new TextBox();

        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<XBindTwoWayLoopProbe, XBindTwoWayLoopProbe, TextBox>(
            static (source, _, _) => source.Alias,
            new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, ".", null),
            dependencies:
            [
                new SourceGenBindingDependency(SourceGenBindingSourceKind.Root, nameof(XBindTwoWayLoopProbe.Alias), null)
            ],
            mode: BindingMode.TwoWay,
            bindBack: static (source, value) => source.Alias = SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<string>(value),
            bindBackValueType: typeof(string),
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Default,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: TextBox.TextProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, TextBox.TextProperty, binding, target);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Catalog alias", target.Text);

        SourceGenMarkupExtensionRuntime.StopTrackingXBind(root);
        Dispatcher.UIThread.RunJobs();

        root.Alias = "Updated alias";
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("Updated alias", target.Text);

        SourceGenMarkupExtensionRuntime.InitializeXBind(root);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Updated alias", target.Text);
    }

    [AvaloniaFact]
    public void StopTrackingXBind_Cancels_Deferred_Ancestor_Attachment()
    {
        var root = new UserControl
        {
            DataContext = new DeferredAnchorViewModel()
        };
        var panel = new StackPanel();
        root.Content = panel;

        var anchor = new Border();
        var target = new DeferredAncestorBindingTarget();
        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<UserControl, UserControl, DeferredAncestorBindingTarget>(
            static (source, _, _) => ((DeferredAnchorViewModel)source.DataContext!).Message,
            new SourceGenBindingDependency(
                SourceGenBindingSourceKind.FindAncestor,
                ".",
                null,
                new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(UserControl),
                    AncestorLevel = 1
                }),
            dependencies: null,
            mode: BindingMode.OneWay,
            bindBack: null,
            bindBackValueType: null,
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Default,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: DeferredAncestorBindingTarget.ValueProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, DeferredAncestorBindingTarget.ValueProperty, binding, anchor);
        Dispatcher.UIThread.RunJobs();

        SourceGenMarkupExtensionRuntime.StopTrackingXBind(root);
        Dispatcher.UIThread.RunJobs();

        var window = new Window
        {
            Content = root
        };

        try
        {
            panel.Children.Add(anchor);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(target.Value);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void ResetXBind_Cancels_Deferred_Ancestor_Attachment()
    {
        var root = new UserControl
        {
            DataContext = new DeferredAnchorViewModel()
        };
        var panel = new StackPanel();
        root.Content = panel;

        var anchor = new Border();
        var target = new DeferredAncestorBindingTarget();
        var binding = SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<UserControl, UserControl, DeferredAncestorBindingTarget>(
            static (source, _, _) => ((DeferredAnchorViewModel)source.DataContext!).Message,
            new SourceGenBindingDependency(
                SourceGenBindingSourceKind.FindAncestor,
                ".",
                null,
                new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(UserControl),
                    AncestorLevel = 1
                }),
            dependencies: null,
            mode: BindingMode.OneWay,
            bindBack: null,
            bindBackValueType: null,
            converter: null,
            converterCulture: null,
            converterParameter: null,
            stringFormat: null,
            fallbackValue: null,
            targetNullValue: null,
            delay: 0,
            updateSourceTrigger: UpdateSourceTrigger.Default,
            priority: BindingPriority.LocalValue,
            parentServiceProvider: null,
            rootObject: root,
            intermediateRootObject: root,
            targetObject: target,
            targetProperty: DeferredAncestorBindingTarget.ValueProperty,
            baseUri: "avares://Demo/MainView.axaml",
            parentStack: null);

        SourceGenMarkupExtensionRuntime.ApplyBinding(target, DeferredAncestorBindingTarget.ValueProperty, binding, anchor);
        Dispatcher.UIThread.RunJobs();

        SourceGenMarkupExtensionRuntime.ResetXBind(root);
        Dispatcher.UIThread.RunJobs();

        var window = new Window
        {
            Content = root
        };

        try
        {
            panel.Children.Add(anchor);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(target.Value);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void WaitForDispatcherCondition(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
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

    private sealed class DeferredAncestorBindingTarget : AvaloniaObject
    {
        public static readonly DirectProperty<DeferredAncestorBindingTarget, string?> ValueProperty =
            AvaloniaProperty.RegisterDirect<DeferredAncestorBindingTarget, string?>(
                nameof(Value),
                static target => target.Value,
                static (target, value) => target.SetValueCore(value));

        private string? _value;

        public string? Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }

        private void SetValueCore(string? value)
        {
            SetAndRaise(ValueProperty, ref _value, value);
        }
    }

    private sealed class XBindTwoWayLoopProbe : AvaloniaObject
    {
        public static readonly DirectProperty<XBindTwoWayLoopProbe, string?> AliasProperty =
            AvaloniaProperty.RegisterDirect<XBindTwoWayLoopProbe, string?>(
                nameof(Alias),
                static target => target.Alias,
                static (target, value) => target.Alias = value);

        private readonly int _aliasSetLimit;
        private string? _alias;

        public XBindTwoWayLoopProbe(string? alias, int aliasSetLimit = 8)
        {
            _alias = alias;
            _aliasSetLimit = aliasSetLimit;
        }

        public int AliasSetCount { get; private set; }

        public string? Alias
        {
            get => _alias;
            set
            {
                AliasSetCount++;
                if (AliasSetCount > _aliasSetLimit)
                {
                    throw new InvalidOperationException("x:Bind bind-back re-entered the source setter.");
                }

                SetAndRaise(AliasProperty, ref _alias, value);
            }
        }

        public void ResetAliasSetCount()
        {
            AliasSetCount = 0;
        }
    }

    private sealed class XBindNormalizingLoopProbe : AvaloniaObject
    {
        public static readonly DirectProperty<XBindNormalizingLoopProbe, string?> SearchDraftProperty =
            AvaloniaProperty.RegisterDirect<XBindNormalizingLoopProbe, string?>(
                nameof(SearchDraft),
                static target => target.SearchDraft,
                static (target, value) => target.SearchDraft = value);

        private readonly int _bindBackLimit;
        private string? _searchDraft;

        public XBindNormalizingLoopProbe(string? searchDraft, int bindBackLimit = 8)
        {
            _searchDraft = searchDraft;
            _bindBackLimit = bindBackLimit;
        }

        public int BindBackCount { get; private set; }

        public string? SearchDraft
        {
            get => _searchDraft;
            set => SetAndRaise(SearchDraftProperty, ref _searchDraft, value);
        }

        public void ApplySearchDraft(string? value)
        {
            BindBackCount++;
            if (BindBackCount > _bindBackLimit)
            {
                throw new InvalidOperationException("x:Bind explicit bind-back re-entered.");
            }

            SearchDraft = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public void ResetBindBackCount()
        {
            BindBackCount = 0;
        }
    }

    private sealed class XBindConverterProbe
    {
        public XBindConverterProbe(int count)
        {
            Count = count;
        }

        public int Count { get; }
    }

    private sealed class XBindRawValueAwareConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value switch
            {
                int intValue => intValue + 1,
                string stringValue => "wrong:" + stringValue,
                _ => value
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value;
        }
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

    private sealed class AllTrueMultiValueConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return values.All(static value => value is true);
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

    private sealed class DeferredKeyBindingViewModel
    {
        public DeferredKeyBindingViewModel()
        {
            Trigger = new TestDelegateCommand(parameter =>
            {
                ExecuteCount++;
                LastParameter = parameter;
            });
        }

        public ICommand Trigger { get; }

        public object Payload { get; } = new object();

        public int ExecuteCount { get; private set; }

        public object? LastParameter { get; private set; }
    }

    private sealed class TestDelegateCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public TestDelegateCommand(Action<object?> execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

}
