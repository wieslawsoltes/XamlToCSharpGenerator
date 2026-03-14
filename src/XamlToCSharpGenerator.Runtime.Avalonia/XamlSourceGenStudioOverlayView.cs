using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.ComponentModel;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioOverlayView : UserControl
{
    private const int LayoutRefreshIntervalMilliseconds = 120;

    private static readonly IBrush HoverAdornerBorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 195, 45));
    private static readonly IBrush HoverAdornerFillBrush = new SolidColorBrush(Color.FromArgb(28, 255, 195, 45));
    private static readonly IBrush SelectionAdornerBorderBrush = new SolidColorBrush(Color.FromArgb(255, 86, 139, 255));
    private static readonly IBrush SelectionAdornerFillBrush = new SolidColorBrush(Color.FromArgb(24, 86, 139, 255));
    private static readonly IBrush SelectionLabelBackground = new SolidColorBrush(Color.FromArgb(220, 86, 139, 255));
    private static readonly IBrush HoverLabelBackground = new SolidColorBrush(Color.FromArgb(220, 153, 112, 0));

    private Border? _liveSurfacePanel;
    private TextBlock? _liveModeText;
    private XamlSourceGenStudioShellViewModel? _viewModel;
    private readonly StyledElement? _liveSurfaceDataContextSource;
    private readonly object? _initialLiveSurfaceDataContext;
    private Grid? _liveLayer;
    private ContentControl? _livePresenter;
    private Canvas? _adornerCanvas;
    private Border? _hoverAdorner;
    private Border? _selectionAdorner;
    private Border? _hoverLabelHost;
    private Border? _selectionLabelHost;
    private TextBlock? _hoverLabelText;
    private TextBlock? _selectionLabelText;
    private Control? _hoveredControl;
    private Control? _selectedControl;
    private DispatcherTimer? _layoutRefreshTimer;
    private bool _layoutRefreshRequested;

    public XamlSourceGenStudioOverlayView(
        object? liveAppContent,
        StyledElement? liveSurfaceDataContextSource,
        object? initialLiveSurfaceDataContext = null)
    {
        _liveSurfaceDataContextSource = liveSurfaceDataContextSource;
        _initialLiveSurfaceDataContext = initialLiveSurfaceDataContext;
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
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,320"),
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

        var hitTestMode = new ComboBox
        {
            Width = 130
        };
        hitTestMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.HitTestModes)));
        hitTestMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.HitTestMode), BindingMode.TwoWay));
        Grid.SetColumn(hitTestMode, 3);
        grid.Children.Add(hitTestMode);

        var state = new TextBlock
        {
            TextAlignment = Avalonia.Media.TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        state.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StudioStateText)));
        Grid.SetColumn(state, 4);
        grid.Children.Add(state);

        var refresh = new Button { Content = "Refresh" };
        refresh.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RefreshCommand)));
        Grid.SetColumn(refresh, 5);
        grid.Children.Add(refresh);

        var undo = new Button { Content = "Undo" };
        undo.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.UndoCommand)));
        Grid.SetColumn(undo, 6);
        grid.Children.Add(undo);

        var redo = new Button { Content = "Redo" };
        redo.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RedoCommand)));
        Grid.SetColumn(redo, 7);
        grid.Children.Add(redo);

        var apply = new Button { Content = "Apply XAML" };
        apply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyXamlCommand)));
        Grid.SetColumn(apply, 8);
        grid.Children.Add(apply);

        var scopeSelector = new ComboBox
        {
            Width = 260,
            ItemTemplate = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenStudioScopeDescriptor>(
                static scope => scope.ScopeKind + ": " + scope.DisplayName)
        };
        scopeSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Scopes)));
        scopeSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedScope), BindingMode.TwoWay));
        Grid.SetColumn(scopeSelector, 9);
        grid.Children.Add(scopeSelector);

        var remoteState = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Right,
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.Gray
        };
        remoteState.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RemoteStatusText)));
        remoteState.Bind(ToolTip.TipProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RemoteVncEndpoint)));
        Grid.SetColumn(remoteState, 10);
        grid.Children.Add(remoteState);

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
                        new TextBlock
                        {
                            Text = item.DisplayName,
                            FontWeight = item.IsSelected ? FontWeight.SemiBold : FontWeight.Normal
                        },
                        new TextBlock
                        {
                            Text = item.Id,
                            FontSize = 11,
                            Foreground = Avalonia.Media.Brushes.Gray
                        },
                        new TextBlock
                        {
                            Text = item.DescendantCount > 0 ? "(" + item.DescendantCount + ")" : string.Empty,
                            FontSize = 11,
                            Foreground = Avalonia.Media.Brushes.DarkGray
                        }
                    }
                },
                item => item.Children)
        };
        tree.Styles.Add(new Style(static selector => selector.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(SourceGenHotDesignElementNode.IsExpanded)))
            }
        });
        tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.DisplayElements)));
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

        _livePresenter = new ContentControl
        {
            Content = liveAppContent
        };
        if (_liveSurfaceDataContextSource is not null)
        {
            _livePresenter.Bind(
                StyledElement.DataContextProperty,
                new Binding(nameof(StyledElement.DataContext))
                {
                    Mode = BindingMode.OneWay,
                    Source = _liveSurfaceDataContextSource
                });
        }
        else if (_initialLiveSurfaceDataContext is not null)
        {
            _livePresenter.DataContext = _initialLiveSurfaceDataContext;
        }

        _liveLayer = new Grid
        {
            ClipToBounds = true
        };
        _liveLayer.AddHandler(
            InputElement.PointerPressedEvent,
            OnLiveSurfacePointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _liveLayer.AddHandler(
            InputElement.PointerMovedEvent,
            OnLiveSurfacePointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _liveLayer.AddHandler(
            InputElement.PointerExitedEvent,
            OnLiveSurfacePointerExited,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _liveLayer.LayoutUpdated += OnLiveLayerLayoutUpdated;
        _liveLayer.Children.Add(_livePresenter);

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
        _liveLayer.Children.Add(overlayLayer);

        _adornerCanvas = new Canvas
        {
            IsHitTestVisible = false
        };
        _hoverAdorner = CreateAdornerBorder(HoverAdornerBorderBrush, HoverAdornerFillBrush, thickness: 1);
        _selectionAdorner = CreateAdornerBorder(SelectionAdornerBorderBrush, SelectionAdornerFillBrush, thickness: 2);
        _hoverLabelText = CreateAdornerLabelText();
        _selectionLabelText = CreateAdornerLabelText();
        _hoverLabelHost = CreateAdornerLabelHost(_hoverLabelText, HoverLabelBackground);
        _selectionLabelHost = CreateAdornerLabelHost(_selectionLabelText, SelectionLabelBackground);

        _adornerCanvas.Children.Add(_hoverAdorner);
        _adornerCanvas.Children.Add(_selectionAdorner);
        _adornerCanvas.Children.Add(_hoverLabelHost);
        _adornerCanvas.Children.Add(_selectionLabelHost);
        _liveLayer.Children.Add(_adornerCanvas);

        Grid.SetRow(_liveLayer, 1);
        grid.Children.Add(_liveLayer);

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
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        var summary = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.Gray
        };
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertySummaryText)));
        grid.Children.Add(summary);

        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,120,120,Auto"),
            ColumnSpacing = 6
        };
        Grid.SetRow(filters, 1);

        var propertySearch = new TextBox
        {
            Watermark = "Filter by name/value/type/category"
        };
        propertySearch.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertySearchText), BindingMode.TwoWay));
        filters.Children.Add(propertySearch);

        var categoryFilter = new ComboBox
        {
            Width = 120
        };
        categoryFilter.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyCategories)));
        categoryFilter.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedPropertyCategory), BindingMode.TwoWay));
        Grid.SetColumn(categoryFilter, 1);
        filters.Children.Add(categoryFilter);

        var sourceFilter = new ComboBox
        {
            Width = 120
        };
        sourceFilter.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertySources)));
        sourceFilter.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedPropertySource), BindingMode.TwoWay));
        Grid.SetColumn(sourceFilter, 2);
        filters.Children.Add(sourceFilter);

        var setOnly = new CheckBox
        {
            Content = "Set only",
            VerticalAlignment = VerticalAlignment.Center
        };
        setOnly.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ShowOnlySetProperties), BindingMode.TwoWay));
        Grid.SetColumn(setOnly, 3);
        filters.Children.Add(setOnly);
        grid.Children.Add(filters);

        var propertyList = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignPropertyEntry>(
                (property, _) => new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    ColumnSpacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = property.IsPinned ? "★" : "☆",
                            Foreground = property.IsPinned ? Brushes.Goldenrod : Brushes.Gray,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new StackPanel
                        {
                            Spacing = 2,
                            [Grid.ColumnProperty] = 1,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = property.Name,
                                    FontWeight = FontWeight.SemiBold
                                },
                                new TextBlock
                                {
                                    Text = property.Category + " | " + property.EditorKind,
                                    FontSize = 11,
                                    Foreground = Brushes.Gray
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = property.Value ?? string.Empty,
                            Foreground = Brushes.LightGray,
                            MaxWidth = 140,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            [Grid.ColumnProperty] = 2
                        },
                        new Border
                        {
                            Background = property.IsSet
                                ? new SolidColorBrush(Color.FromArgb(60, 86, 139, 255))
                                : new SolidColorBrush(Color.FromArgb(45, 120, 120, 120)),
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(4, 1),
                            VerticalAlignment = VerticalAlignment.Center,
                            [Grid.ColumnProperty] = 3,
                            Child = new TextBlock
                            {
                                Text = property.Source,
                                FontSize = 11
                            }
                        }
                    }
                },
                supportsRecycling: true)
        };
        propertyList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.FilteredProperties)));
        propertyList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedProperty), BindingMode.TwoWay));
        Grid.SetRow(propertyList, 2);
        grid.Children.Add(propertyList);

        var propertyName = new TextBox { Watermark = "Property" };
        propertyName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyName), BindingMode.TwoWay));
        Grid.SetRow(propertyName, 3);
        grid.Children.Add(propertyName);

        var propertyValue = new TextBox { Watermark = "Value" };
        propertyValue.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyValue), BindingMode.TwoWay));
        Grid.SetRow(propertyValue, 4);
        grid.Children.Add(propertyValue);

        var quickSetList = new ListBox
        {
            Height = 100,
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignPropertyQuickSet>(
                (quickSet, _) => new TextBlock
                {
                    Text = quickSet.Label + " = " + quickSet.Value
                },
                supportsRecycling: true)
        };
        quickSetList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedPropertyQuickSets)));
        quickSetList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedQuickSet), BindingMode.TwoWay));
        Grid.SetRow(quickSetList, 5);
        grid.Children.Add(quickSetList);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        var quickApply = new Button { Content = "Apply Quick Set" };
        quickApply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyQuickSetCommand)));
        tools.Children.Add(quickApply);

        var pin = new Button { Content = "Pin/Unpin Selected" };
        pin.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.TogglePropertyPinCommand)));
        pin.Bind(Button.CommandParameterProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedProperty)));
        tools.Children.Add(pin);
        Grid.SetRow(tools, 6);
        grid.Children.Add(tools);

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
        Grid.SetRow(actions, 7);
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
            ItemTemplate = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenHotDesignDocumentDescriptor>(
                static document => document.BuildUri)
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
            ItemTemplate = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenHotDesignDocumentDescriptor>(
                static document => document.BuildUri)
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
        StopLayoutRefreshTimer(dispose: true);

        if (_liveLayer is not null)
        {
            _liveLayer.LayoutUpdated -= OnLiveLayerLayoutUpdated;
        }

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

        if (e.PropertyName == nameof(XamlSourceGenStudioShellViewModel.HitTestMode))
        {
            _hoveredControl = null;
            RefreshAdorners();
            RefreshLiveTreeProjection();
        }

        if (e.PropertyName == nameof(XamlSourceGenStudioShellViewModel.SelectedElement))
        {
            SynchronizeSelectionFromWorkspace();
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

        if (interactive)
        {
            StopLayoutRefreshTimer();
            _hoveredControl = null;
            _selectedControl = null;
            HideAdorners();
            _viewModel?.ClearLiveElementTree();
        }
        else
        {
            RefreshAdorners();
            RefreshLiveTreeProjection();
        }
    }

    private void OnLiveSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsInteractiveMode)
        {
            return;
        }

        var control = ResolveControlAtPointer(e);
        if (control is null)
        {
            return;
        }

        if (_viewModel.TryHandleLiveSurfacePointerPressed(control))
        {
            _selectedControl = control;
            _hoveredControl = control;
            RefreshSelectionAdorner();
            RefreshHoverAdorner();
            e.Handled = true;
        }
    }

    private void OnLiveSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsInteractiveMode)
        {
            return;
        }

        var control = ResolveControlAtPointer(e);
        if (ReferenceEquals(control, _hoveredControl))
        {
            return;
        }

        _hoveredControl = control;
        RefreshHoverAdorner();
    }

    private void OnLiveSurfacePointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredControl is null)
        {
            return;
        }

        _hoveredControl = null;
        RefreshHoverAdorner();
    }

    private void OnLiveLayerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_viewModel?.IsInteractiveMode != false)
        {
            StopLayoutRefreshTimer();
            return;
        }

        ScheduleLayoutRefresh();
    }

    private void ScheduleLayoutRefresh()
    {
        _layoutRefreshTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(LayoutRefreshIntervalMilliseconds),
            DispatcherPriority.Background,
            OnLayoutRefreshTimerTick);

        _layoutRefreshRequested = true;

        if (!_layoutRefreshTimer.IsEnabled)
        {
            _layoutRefreshTimer.Start();
        }
    }

    private void OnLayoutRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_viewModel?.IsInteractiveMode != false)
        {
            StopLayoutRefreshTimer();
            return;
        }

        if (_layoutRefreshTimer is not null)
        {
            if (!_layoutRefreshRequested)
            {
                _layoutRefreshTimer.Stop();
                return;
            }
        }

        _layoutRefreshRequested = false;

        if (_liveLayer is null || _viewModel is null)
        {
            return;
        }

        RefreshAdorners();
        RefreshLiveTreeProjection();

        if (_layoutRefreshTimer is not null && !_layoutRefreshRequested)
        {
            _layoutRefreshTimer.Stop();
        }
    }

    private void RefreshLiveTreeProjection()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.IsInteractiveMode)
        {
            return;
        }

        if (TryResolveLiveRootControl() is { } liveRootControl)
        {
            _viewModel.UpdateLiveElementTree(liveRootControl);
            return;
        }

        _viewModel.ClearLiveElementTree();
    }

    private Control? TryResolveLiveRootControl()
    {
        if (_livePresenter?.Content is not Visual rootVisual)
        {
            return null;
        }

        if (rootVisual is Control rootControl)
        {
            return rootControl;
        }

        foreach (var visual in rootVisual.GetVisualDescendants())
        {
            if (visual is Control control)
            {
                return control;
            }
        }

        return null;
    }

    private void SynchronizeSelectionFromWorkspace()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.IsInteractiveMode)
        {
            _selectedControl = null;
            RefreshSelectionAdorner();
            return;
        }

        if (_viewModel.SelectedElement is null)
        {
            return;
        }

        var matched = TryFindLiveControlForElement(_viewModel.SelectedElement);
        if (matched is null)
        {
            _selectedControl = null;
            RefreshSelectionAdorner();
            return;
        }

        _selectedControl = matched;
        RefreshSelectionAdorner();
    }

    private void StopLayoutRefreshTimer(bool dispose = false)
    {
        if (_layoutRefreshTimer is not null)
        {
            _layoutRefreshTimer.Stop();
            if (dispose)
            {
                _layoutRefreshTimer = null;
            }
        }

        _layoutRefreshRequested = false;
    }

    private Control? TryFindLiveControlForElement(SourceGenHotDesignElementNode element)
    {
        var liveRoot = TryResolveLiveRootControl();
        if (liveRoot is null || _viewModel is null)
        {
            return null;
        }

        return XamlSourceGenStudioLiveTreeProjectionService.ResolveLiveControlForElement(
            liveRoot,
            _viewModel.HitTestMode,
            element,
            string.IsNullOrWhiteSpace(_viewModel.ActiveBuildUri) ? null : _viewModel.ActiveBuildUri);
    }

    private Control? ResolveControlAtPointer(PointerEventArgs e)
    {
        if (_liveLayer is null || _viewModel is null)
        {
            return TryResolveLiveContentControl(e.Source);
        }

        var point = e.GetPosition(_liveLayer);
        if (_livePresenter is not null &&
            XamlSourceGenStudioHitTestingService.ResolveControlAtPoint(
                _liveLayer,
                _livePresenter,
                point,
                _viewModel.HitTestMode) is { } resolved)
        {
            return resolved;
        }

        return TryResolveLiveContentControl(e.Source);
    }

    private Control? TryResolveLiveContentControl(object? pointerSource)
    {
        var control = TryResolveControl(pointerSource);
        if (control is null || _livePresenter is null)
        {
            return null;
        }

        return IsDescendantOf(control, _livePresenter)
            ? control
            : null;
    }

    private static Control? TryResolveControl(object? pointerSource)
    {
        if (pointerSource is Control directControl)
        {
            return directControl;
        }

        if (pointerSource is not Visual visual)
        {
            return null;
        }

        if (visual is Control visualControl)
        {
            return visualControl;
        }

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is Control control)
            {
                return control;
            }
        }

        return null;
    }

    private static bool IsDescendantOf(Visual candidate, Visual ancestor)
    {
        return XamlSourceGenStudioHitTestingService.IsDescendantOf(candidate, ancestor);
    }

    private static Border CreateAdornerBorder(IBrush borderBrush, IBrush fillBrush, double thickness)
    {
        return new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(thickness),
            Background = fillBrush,
            IsVisible = false,
            IsHitTestVisible = false
        };
    }

    private static TextBlock CreateAdornerLabelText()
    {
        return new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.White
        };
    }

    private static Border CreateAdornerLabelHost(TextBlock labelText, IBrush backgroundBrush)
    {
        return new Border
        {
            Background = backgroundBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = labelText
        };
    }

    private void RefreshAdorners()
    {
        RefreshSelectionAdorner();
        RefreshHoverAdorner();
    }

    private void RefreshSelectionAdorner()
    {
        UpdateAdorner(_selectedControl, _selectionAdorner, _selectionLabelHost, _selectionLabelText, "Selected");
    }

    private void RefreshHoverAdorner()
    {
        if (_hoveredControl is not null && ReferenceEquals(_hoveredControl, _selectedControl))
        {
            UpdateAdorner(null, _hoverAdorner, _hoverLabelHost, _hoverLabelText, "Hover");
            return;
        }

        UpdateAdorner(_hoveredControl, _hoverAdorner, _hoverLabelHost, _hoverLabelText, "Hover");
    }

    private void HideAdorners()
    {
        SetAdornerVisibility(_hoverAdorner, _hoverLabelHost, false);
        SetAdornerVisibility(_selectionAdorner, _selectionLabelHost, false);
    }

    private void UpdateAdorner(
        Control? control,
        Border? adorner,
        Border? labelHost,
        TextBlock? labelText,
        string modeLabel)
    {
        if (adorner is null || labelHost is null || labelText is null || _liveLayer is null || _viewModel?.IsInteractiveMode != false)
        {
            SetAdornerVisibility(adorner, labelHost, false);
            return;
        }

        if (control is null || !TryGetControlBoundsInLayer(control, _liveLayer, out var bounds))
        {
            SetAdornerVisibility(adorner, labelHost, false);
            return;
        }

        adorner.Width = bounds.Width;
        adorner.Height = bounds.Height;
        Canvas.SetLeft(adorner, bounds.X);
        Canvas.SetTop(adorner, bounds.Y);

        labelText.Text = modeLabel + ": " + GetControlDisplayName(control);
        labelHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = labelHost.DesiredSize;
        var labelX = Math.Clamp(bounds.X, 0, Math.Max(0, _liveLayer.Bounds.Width - desired.Width));
        var labelY = bounds.Y - desired.Height - 4;
        if (labelY < 0)
        {
            labelY = Math.Min(_liveLayer.Bounds.Height - desired.Height, bounds.Bottom + 4);
        }

        Canvas.SetLeft(labelHost, Math.Max(0, labelX));
        Canvas.SetTop(labelHost, Math.Max(0, labelY));
        SetAdornerVisibility(adorner, labelHost, true);
    }

    private static string GetControlDisplayName(Control control)
    {
        var typeName = control.GetType().Name;
        if (string.IsNullOrWhiteSpace(control.Name))
        {
            return typeName;
        }

        return typeName + " (" + control.Name + ")";
    }

    private static void SetAdornerVisibility(Border? adorner, Border? labelHost, bool isVisible)
    {
        if (adorner is not null)
        {
            adorner.IsVisible = isVisible;
        }

        if (labelHost is not null)
        {
            labelHost.IsVisible = isVisible;
        }
    }

    private static bool TryGetControlBoundsInLayer(Control control, Visual layer, out Rect bounds)
    {
        return XamlSourceGenStudioHitTestingService.TryGetControlBoundsInLayer(control, layer, out bounds);
    }
}
