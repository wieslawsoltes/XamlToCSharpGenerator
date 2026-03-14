using System;
using System.Linq;
using System.Collections.Specialized;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenStudioViewTests
{
    [AvaloniaFact]
    public void StudioShellView_Shows_With_Empty_Scope_And_Document_Selection()
    {
        ResetRuntimeState();
        EnsureFluentTheme();

        using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
        var window = new Window
        {
            Width = 1280,
            Height = 900,
            Content = new XamlSourceGenStudioShellView
            {
                DataContext = viewModel
            }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(window.GetVisualDescendants().OfType<ComboBox>());
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }

            ResetRuntimeState();
        }
    }

    [AvaloniaFact]
    public void StudioTextTemplate_Renders_Empty_Text_For_Null_Item()
    {
        var template = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenStudioScopeDescriptor>(
            static scope => scope.ScopeKind + ": " + scope.DisplayName);

        var nullControl = ((IDataTemplate)template).Build(null);
        var populatedControl = ((IDataTemplate)template).Build(
            new SourceGenStudioScopeDescriptor(SourceGenStudioScopeKind.RootControl, "scope-1", "Demo"));

        Assert.Equal(string.Empty, Assert.IsType<TextBlock>(nullControl).Text);
        Assert.Equal("RootControl: Demo", Assert.IsType<TextBlock>(populatedControl).Text);
    }

    [AvaloniaFact]
    public void StudioOverlayView_Shows_With_Simple_Live_Surface()
    {
        ResetRuntimeState();
        EnsureFluentTheme();

        using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
        var window = new Window
        {
            Width = 1280,
            Height = 900,
            Content = new XamlSourceGenStudioOverlayView(
                liveAppContent: new Border
                {
                    Child = new TextBlock
                    {
                        Text = "Live surface"
                    }
                },
                liveSurfaceDataContextSource: null)
            {
                DataContext = viewModel
            }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(window.GetVisualDescendants().OfType<ComboBox>());
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }

            ResetRuntimeState();
        }
    }

    [AvaloniaFact]
    public void UpdateLiveElementTree_Does_Not_Republish_Equivalent_Projection()
    {
        ResetRuntimeState();

        using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
        var root = new StackPanel
        {
            Name = "RootPanel",
            Children =
            {
                new Button
                {
                    Name = "ActionButton",
                    Content = "Run"
                }
            }
        };

        var collectionChanges = 0;
        NotifyCollectionChangedEventHandler handler = (_, _) => collectionChanges++;
        viewModel.DisplayElements.CollectionChanged += handler;

        try
        {
            viewModel.UpdateLiveElementTree(root);

            var initialChangeCount = collectionChanges;
            var initialRootNode = Assert.Single(viewModel.DisplayElements);
            var initialBuildCount = GetLiveProjectionBuildCount(viewModel);

            viewModel.UpdateLiveElementTree(root);

            Assert.Equal(initialChangeCount, collectionChanges);
            Assert.Same(initialRootNode, Assert.Single(viewModel.DisplayElements));
            Assert.Equal(initialBuildCount, GetLiveProjectionBuildCount(viewModel));
        }
        finally
        {
            viewModel.DisplayElements.CollectionChanged -= handler;
            ResetRuntimeState();
        }
    }

    [AvaloniaFact]
    public void UpdateLiveElementTree_Rebuilds_When_Same_Root_Instance_Mutates()
    {
        ResetRuntimeState();

        using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
        var root = new StackPanel
        {
            Name = "RootPanel",
            Children =
            {
                new Button
                {
                    Name = "ActionButton",
                    Content = "Run"
                }
            }
        };

        viewModel.UpdateLiveElementTree(root);

        var initialRootNode = Assert.Single(viewModel.DisplayElements);
        Assert.Single(initialRootNode.Children);
        var initialBuildCount = GetLiveProjectionBuildCount(viewModel);

        root.Children.Insert(0, new TextBlock
        {
            Name = "InsertedLabel",
            Text = "Inserted"
        });

        viewModel.UpdateLiveElementTree(root);

        var updatedRootNode = Assert.Single(viewModel.DisplayElements);
        Assert.Equal(2, updatedRootNode.Children.Count);
        Assert.Equal(initialBuildCount + 1, GetLiveProjectionBuildCount(viewModel));

        ResetRuntimeState();
    }

    private static int GetLiveProjectionBuildCount(XamlSourceGenStudioShellViewModel viewModel)
    {
        var field = typeof(XamlSourceGenStudioShellViewModel).GetField(
            "_liveProjectionBuildCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<int>(field!.GetValue(viewModel));
    }

    private static void EnsureFluentTheme()
    {
        var application = Application.Current ?? throw new InvalidOperationException("Avalonia application is not initialized.");

        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Insert(0, new FluentTheme());
        }

        application.RequestedThemeVariant = ThemeVariant.Default;
    }

    private static void ResetRuntimeState()
    {
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.StopSession();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }
}
