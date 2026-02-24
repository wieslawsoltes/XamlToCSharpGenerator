using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.ComponentModel;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioOverlayView : UserControl
{
    private Border? _liveSurfacePanel;
    private TextBlock? _liveModeText;
    private XamlSourceGenStudioShellViewModel? _viewModel;
    private readonly object? _liveSurfaceDataContext;

    public XamlSourceGenStudioOverlayView(object? liveAppContent, object? liveSurfaceDataContext)
    {
        _liveSurfaceDataContext = liveSurfaceDataContext;
        BuildContent(liveAppContent);
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void BuildContent(object? liveAppContent)
    {
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        rootGrid.Children.Add(BuildToolbar());

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*,420"),
            ColumnSpacing = 8,
            Margin = new Thickness(8)
        };
        Grid.SetRow(body, 1);

        body.Children.Add(BuildLeftPanel());

        var liveSurface = BuildLiveSurface(liveAppContent);
        Grid.SetColumn(liveSurface, 1);
        body.Children.Add(liveSurface);

        var right = BuildRightPanel();
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        rootGrid.Children.Add(body);
        Content = rootGrid;
    }

    private static Control BuildToolbar()
    {
        var toolbar = new Border
        {
            Padding = new Thickness(8),
            BorderBrush = Avalonia.Media.Brushes.SteelBlue,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };
        toolbar.Child = grid;

        grid.Children.Add(new TextBlock
        {
            Text = "SourceGen Studio Overlay",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var mode = new ComboBox
        {
            Width = 130
        };
        mode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.WorkspaceModes)));
        mode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.WorkspaceMode), BindingMode.TwoWay));
        Grid.SetColumn(mode, 1);
        grid.Children.Add(mode);

        var filter = new ComboBox
        {
            Width = 130
        };
        filter.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyFilterModes)));
        filter.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyFilterMode), BindingMode.TwoWay));
        Grid.SetColumn(filter, 2);
        grid.Children.Add(filter);

        var state = new TextBlock
        {
            TextAlignment = Avalonia.Media.TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        state.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StudioStateText)));
        Grid.SetColumn(state, 3);
        grid.Children.Add(state);

        var refresh = new Button { Content = "Refresh" };
        refresh.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RefreshCommand)));
        Grid.SetColumn(refresh, 4);
        grid.Children.Add(refresh);

        var undo = new Button { Content = "Undo" };
        undo.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.UndoCommand)));
        Grid.SetColumn(undo, 5);
        grid.Children.Add(undo);

        var redo = new Button { Content = "Redo" };
        redo.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RedoCommand)));
        Grid.SetColumn(redo, 6);
        grid.Children.Add(redo);

        var apply = new Button { Content = "Apply XAML" };
        apply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyXamlCommand)));
        Grid.SetColumn(apply, 7);
        grid.Children.Add(apply);

        var scopeSelector = new ComboBox
        {
            Width = 260,
            ItemTemplate = new FuncDataTemplate<SourceGenStudioScopeDescriptor>(
                (scope, _) => new TextBlock
                {
                    Text = scope.ScopeKind + ": " + scope.DisplayName
                },
                supportsRecycling: true)
        };
        scopeSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Scopes)));
        scopeSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedScope), BindingMode.TwoWay));
        Grid.SetColumn(scopeSelector, 8);
        grid.Children.Add(scopeSelector);

        return toolbar;
    }

    private static Control BuildLeftPanel()
    {
        var panel = new Border
        {
            BorderBrush = Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,*,Auto,Auto"),
            RowSpacing = 8
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Elements",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        var search = new TextBox
        {
            Watermark = "Search elements/toolbox"
        };
        search.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SearchText), BindingMode.TwoWay));
        Grid.SetRow(search, 1);
        grid.Children.Add(search);

        var tree = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<SourceGenHotDesignElementNode>(
                (item, _) => new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = item.DisplayName },
                        new TextBlock
                        {
                            Text = item.Id,
                            FontSize = 11,
                            Foreground = Avalonia.Media.Brushes.Gray
                        }
                    }
                },
                item => item.Children)
        };
        tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Elements)));
        tree.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedElement), BindingMode.TwoWay));
        Grid.SetRow(tree, 2);
        grid.Children.Add(tree);

        var toolbox = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignToolboxItem>(
                (item, _) => new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = item.DisplayName },
                        new TextBlock
                        {
                            Text = item.Category,
                            FontSize = 11,
                            Foreground = Avalonia.Media.Brushes.Gray
                        }
                    }
                },
                supportsRecycling: true)
        };
        toolbox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ToolboxItems)));
        toolbox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedToolboxItem), BindingMode.TwoWay));
        Grid.SetRow(toolbox, 3);
        grid.Children.Add(toolbox);

        var elementName = new TextBox
        {
            Watermark = "Element name"
        };
        elementName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.NewElementName), BindingMode.TwoWay));
        Grid.SetRow(elementName, 4);
        grid.Children.Add(elementName);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var insert = new Button { Content = "Insert" };
        insert.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.InsertElementCommand)));
        actions.Children.Add(insert);

        var remove = new Button { Content = "Remove" };
        remove.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RemoveElementCommand)));
        actions.Children.Add(remove);
        Grid.SetRow(actions, 5);
        grid.Children.Add(actions);

        panel.Child = grid;
        return panel;
    }

    private Control BuildLiveSurface(object? liveAppContent)
    {
        _liveSurfacePanel = new Border
        {
            BorderBrush = Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Live App Surface",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        var presenter = new ContentControl
        {
            Content = liveAppContent
        };
        if (_liveSurfaceDataContext is not null)
        {
            presenter.DataContext = _liveSurfaceDataContext;
        }

        var liveLayer = new Grid();
        liveLayer.AddHandler(
            InputElement.PointerPressedEvent,
            OnLiveSurfacePointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        liveLayer.Children.Add(presenter);

        _liveModeText = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        };

        var modeBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 20, 28, 44)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 86, 139, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _liveModeText
        };

        var overlayLayer = new Border
        {
            Background = Brushes.Transparent,
            Child = modeBanner,
            IsHitTestVisible = false
        };
        liveLayer.Children.Add(overlayLayer);

        Grid.SetRow(liveLayer, 1);
        grid.Children.Add(liveLayer);

        var status = new TextBlock
        {
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.Gray,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StatusMessage)));
        Grid.SetRow(status, 2);
        grid.Children.Add(status);

        _liveSurfacePanel.Child = grid;
        UpdateLiveSurfaceModeVisuals();
        return _liveSurfacePanel;
    }

    private static Control BuildRightPanel()
    {
        var panel = new Border
        {
            BorderBrush = Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var tabs = new TabControl();
        tabs.Items.Add(BuildPropertiesTab());
        tabs.Items.Add(BuildXamlEditorTab());
        tabs.Items.Add(BuildTemplateEditorTab());
        tabs.Items.Add(BuildOperationsTab());
        panel.Child = tabs;

        return panel;
    }

    private static TabItem BuildPropertiesTab()
    {
        var tab = new TabItem
        {
            Header = "Properties"
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        var propertyList = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignPropertyEntry>(
                (property, _) => new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = property.Name
                        },
                        new TextBlock
                        {
                            Text = property.Value ?? string.Empty,
                            Foreground = Avalonia.Media.Brushes.Gray,
                            [Grid.ColumnProperty] = 1
                        }
                    }
                },
                supportsRecycling: true)
        };
        propertyList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Properties)));
        propertyList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedProperty), BindingMode.TwoWay));
        grid.Children.Add(propertyList);

        var propertyName = new TextBox { Watermark = "Property" };
        propertyName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyName), BindingMode.TwoWay));
        Grid.SetRow(propertyName, 1);
        grid.Children.Add(propertyName);

        var propertyValue = new TextBox { Watermark = "Value" };
        propertyValue.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyValue), BindingMode.TwoWay));
        Grid.SetRow(propertyValue, 2);
        grid.Children.Add(propertyValue);

        var quickSetList = new ListBox
        {
            Height = 90,
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignPropertyQuickSet>(
                (quickSet, _) => new TextBlock
                {
                    Text = quickSet.Label + " = " + quickSet.Value
                },
                supportsRecycling: true)
        };
        quickSetList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedPropertyQuickSets)));
        quickSetList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedQuickSet), BindingMode.TwoWay));
        Grid.SetRow(quickSetList, 3);
        grid.Children.Add(quickSetList);

        var quickApply = new Button { Content = "Apply Quick Set" };
        quickApply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyQuickSetCommand)));
        Grid.SetRow(quickApply, 4);
        grid.Children.Add(quickApply);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var apply = new Button { Content = "Apply" };
        apply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyPropertyCommand)));
        actions.Children.Add(apply);
        var remove = new Button { Content = "Remove" };
        remove.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RemovePropertyCommand)));
        actions.Children.Add(remove);
        Grid.SetRow(actions, 5);
        grid.Children.Add(actions);

        tab.Content = grid;
        return tab;
    }

    private static TabItem BuildXamlEditorTab()
    {
        var tab = new TabItem
        {
            Header = "XAML Editor"
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        var documents = new ComboBox
        {
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignDocumentDescriptor>(
                (document, _) => new TextBlock
                {
                    Text = document.BuildUri
                },
                supportsRecycling: true)
        };
        documents.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Documents)));
        documents.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedDocument), BindingMode.TwoWay));
        grid.Children.Add(documents);

        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = Avalonia.Media.FontFamily.Parse("Consolas"),
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
        };
        editor.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.XamlText), BindingMode.TwoWay));
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);

        var apply = new Button
        {
            Content = "Apply XAML",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        apply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyXamlCommand)));
        Grid.SetRow(apply, 2);
        grid.Children.Add(apply);

        tab.Content = grid;
        return tab;
    }

    private static TabItem BuildTemplateEditorTab()
    {
        var tab = new TabItem
        {
            Header = "Template Editor"
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        var templateDocs = new ComboBox
        {
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignDocumentDescriptor>(
                (document, _) => new TextBlock
                {
                    Text = document.BuildUri
                },
                supportsRecycling: true)
        };
        templateDocs.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.TemplateDocuments)));
        templateDocs.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedTemplateDocument), BindingMode.TwoWay));
        grid.Children.Add(templateDocs);

        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = Avalonia.Media.FontFamily.Parse("Consolas"),
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
        };
        editor.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.TemplateXamlText), BindingMode.TwoWay));
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);

        var apply = new Button
        {
            Content = "Apply Template",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        apply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyTemplateXamlCommand)));
        Grid.SetRow(apply, 2);
        grid.Children.Add(apply);

        tab.Content = grid;
        return tab;
    }

    private static TabItem BuildOperationsTab()
    {
        var tab = new TabItem
        {
            Header = "Operations"
        };

        var list = new ListBox();
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StudioOperationLines)));
        tab.Content = list;
        return tab;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (ReferenceEquals(_viewModel, DataContext))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as XamlSourceGenStudioShellViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateLiveSurfaceModeVisuals();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(XamlSourceGenStudioShellViewModel.WorkspaceMode) or
            nameof(XamlSourceGenStudioShellViewModel.LiveSurfaceModeText))
        {
            UpdateLiveSurfaceModeVisuals();
        }
    }

    private void UpdateLiveSurfaceModeVisuals()
    {
        if (_liveModeText is null || _liveSurfacePanel is null)
        {
            return;
        }

        var interactive = _viewModel?.IsInteractiveMode ?? true;
        _liveModeText.Text = _viewModel?.LiveSurfaceModeText ?? "Interactive mode: the app behaves normally.";
        _liveSurfacePanel.BorderBrush = interactive
            ? Brushes.DimGray
            : new SolidColorBrush(Color.FromArgb(255, 86, 139, 255));
    }

    private void OnLiveSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsInteractiveMode)
        {
            return;
        }

        _viewModel.TryHandleLiveSurfacePointerPressed(e.Source);
        e.Handled = true;
    }
}
