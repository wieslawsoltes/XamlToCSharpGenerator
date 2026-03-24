#if NET10_0_OR_GREATER
extern alias axeditor;
#endif

using System;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Data;
using global::Avalonia.Layout;
using global::Avalonia.Styling;
#if NET10_0_OR_GREATER
using AxamlTextEditor = axeditor::XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor;
#endif

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioShellPanel : UserControl
{
    public XamlSourceGenStudioShellPanel()
    {
        BuildContent();
    }

    private void BuildContent()
    {
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("*")
        };

        rootGrid.Children.Add(BuildToolbar());
        var body = BuildBody();
        Grid.SetRow(body, 1);
        rootGrid.Children.Add(body);

        Content = rootGrid;
    }

    private Control BuildToolbar()
    {
        var toolbar = new Border
        {
            Padding = new Thickness(8),
            BorderBrush = global::Avalonia.Media.Brushes.SteelBlue,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("240,130,130,130,260,*,Auto,Auto,Auto,Auto,300"),
                ColumnSpacing = 8
            }
        };

        if (toolbar.Child is not Grid grid)
        {
            return toolbar;
        }

        var scopeSelector = new ComboBox
        {
            Width = 240,
            ItemTemplate = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenStudioScopeDescriptor>(
                static scope => scope.ScopeKind + ": " + scope.DisplayName)
        };
        scopeSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.Scopes)));
        scopeSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedScope), BindingMode.TwoWay));
        grid.Children.Add(scopeSelector);

        var workspaceMode = new ComboBox
        {
            Width = 130
        };
        workspaceMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.WorkspaceModes)));
        workspaceMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.WorkspaceMode), BindingMode.TwoWay));
        Grid.SetColumn(workspaceMode, 1);
        grid.Children.Add(workspaceMode);

        var propertyMode = new ComboBox
        {
            Width = 130
        };
        propertyMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertyFilterModes)));
        propertyMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertyFilterMode), BindingMode.TwoWay));
        Grid.SetColumn(propertyMode, 2);
        grid.Children.Add(propertyMode);

        var hitTestMode = new ComboBox
        {
            Width = 130
        };
        hitTestMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.HitTestModes)));
        hitTestMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.HitTestMode), BindingMode.TwoWay));
        Grid.SetColumn(hitTestMode, 3);
        grid.Children.Add(hitTestMode);

        var documentSelector = new ComboBox
        {
            Width = 260,
            ItemTemplate = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenHotDesignDocumentDescriptor>(
                static document => document.BuildUri)
        };
        documentSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.Documents)));
        documentSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedDocument), BindingMode.TwoWay));
        Grid.SetColumn(documentSelector, 4);
        grid.Children.Add(documentSelector);

        var searchText = new TextBox
        {
            Watermark = "Search elements/toolbox"
        };
        searchText.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.SearchText), BindingMode.TwoWay));
        Grid.SetColumn(searchText, 5);
        grid.Children.Add(searchText);

        var refreshButton = new Button
        {
            Content = "Refresh",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        refreshButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.RefreshCommand)));
        Grid.SetColumn(refreshButton, 6);
        grid.Children.Add(refreshButton);

        var undoButton = new Button
        {
            Content = "Undo",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        undoButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.UndoCommand)));
        Grid.SetColumn(undoButton, 7);
        grid.Children.Add(undoButton);

        var redoButton = new Button
        {
            Content = "Redo",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        redoButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.RedoCommand)));
        Grid.SetColumn(redoButton, 8);
        grid.Children.Add(redoButton);

        var applyButton = new Button
        {
            Content = "Apply XAML",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        applyButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.ApplyXamlCommand)));
        Grid.SetColumn(applyButton, 9);
        grid.Children.Add(applyButton);

        var remoteStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = global::Avalonia.Media.TextAlignment.Right,
            FontSize = 11,
            Foreground = global::Avalonia.Media.Brushes.Gray
        };
        remoteStatus.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.RemoteStatusText)));
        remoteStatus.Bind(ToolTip.TipProperty, new Binding(nameof(XamlSourceGenStudioShellState.RemoteVncEndpoint)));
        Grid.SetColumn(remoteStatus, 10);
        grid.Children.Add(remoteStatus);

        return toolbar;
    }

    private Control BuildBody()
    {
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*,360"),
            ColumnSpacing = 8,
            RowDefinitions = new RowDefinitions("*")
        };

        var left = BuildLeftPanels();
        body.Children.Add(left);

        var center = BuildCenterPanels();
        Grid.SetColumn(center, 1);
        body.Children.Add(center);

        var right = BuildPropertiesPanel();
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        return body;
    }

    private static Control BuildLeftPanels()
    {
        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 8
        };

        var elementsPanel = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };
        var elementsGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };
        elementsGrid.Children.Add(new TextBlock
        {
            Text = "Element Tree",
            FontWeight = global::Avalonia.Media.FontWeight.Bold
        });

        var tree = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<SourceGenHotDesignElementNode>(
                (item, _) =>
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = item.DisplayName,
                                FontWeight = item.IsSelected ? global::Avalonia.Media.FontWeight.SemiBold : global::Avalonia.Media.FontWeight.Normal
                            },
                            new TextBlock
                            {
                                Text = item.Id,
                                FontSize = 11,
                                Foreground = global::Avalonia.Media.Brushes.Gray
                            },
                            new TextBlock
                            {
                                Text = item.DescendantCount > 0 ? "(" + item.DescendantCount + ")" : string.Empty,
                                FontSize = 11,
                                Foreground = global::Avalonia.Media.Brushes.DarkGray
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
        tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.DisplayElements)));
        tree.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedElement), BindingMode.TwoWay));
        Grid.SetRow(tree, 1);
        elementsGrid.Children.Add(tree);

        var removeElementButton = new Button
        {
            Content = "Remove Selected",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        removeElementButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.RemoveElementCommand)));
        Grid.SetRow(removeElementButton, 2);
        elementsGrid.Children.Add(removeElementButton);
        elementsPanel.Child = elementsGrid;
        left.Children.Add(elementsPanel);

        var toolboxPanel = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };
        var toolboxGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            RowSpacing = 8
        };
        toolboxGrid.Children.Add(new TextBlock
        {
            Text = "Toolbox",
            FontWeight = global::Avalonia.Media.FontWeight.Bold
        });

        var toolboxList = new ListBox
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
                            Foreground = global::Avalonia.Media.Brushes.Gray
                        }
                    }
                },
                supportsRecycling: true)
        };
        toolboxList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.ToolboxItems)));
        toolboxList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedToolboxItem), BindingMode.TwoWay));
        Grid.SetRow(toolboxList, 1);
        toolboxGrid.Children.Add(toolboxList);

        var elementName = new TextBox
        {
            Watermark = "Element name"
        };
        elementName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.NewElementName), BindingMode.TwoWay));
        Grid.SetRow(elementName, 2);
        toolboxGrid.Children.Add(elementName);

        var insertButton = new Button
        {
            Content = "Insert Under Selection",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        insertButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.InsertElementCommand)));
        Grid.SetRow(insertButton, 3);
        toolboxGrid.Children.Add(insertButton);
        toolboxPanel.Child = toolboxGrid;
        Grid.SetRow(toolboxPanel, 1);
        left.Children.Add(toolboxPanel);

        return left;
    }

    private Control BuildCenterPanels()
    {
        var centerPanel = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var tabs = new TabControl();
        tabs.Items.Add(BuildCanvasTab());
        tabs.Items.Add(BuildTemplateEditorTab());
        tabs.Items.Add(BuildOperationsTab());
        centerPanel.Child = tabs;

        return centerPanel;
    }

    private TabItem BuildCanvasTab()
    {
        var tab = new TabItem
        {
            Header = "Canvas"
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,Auto,110,Auto,150,Auto,*"),
            ColumnSpacing = 8
        };

        toolbar.Children.Add(new TextBlock
        {
            Text = "Zoom",
            VerticalAlignment = VerticalAlignment.Center
        });

        var zoom = new Slider
        {
            Minimum = 0.3,
            Maximum = 2.5,
            Width = 120
        };
        zoom.Bind(RangeBase.ValueProperty, new Binding(nameof(XamlSourceGenStudioShellState.Zoom), BindingMode.TwoWay));
        Grid.SetColumn(zoom, 1);
        toolbar.Children.Add(zoom);

        var formFactorLabel = new TextBlock
        {
            Text = "Form factor",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(formFactorLabel, 2);
        toolbar.Children.Add(formFactorLabel);

        var formFactor = new TextBox
        {
            Width = 110
        };
        formFactor.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.FormFactor), BindingMode.TwoWay));
        Grid.SetColumn(formFactor, 3);
        toolbar.Children.Add(formFactor);

        var layoutLabel = new TextBlock
        {
            Text = "Layout",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(layoutLabel, 4);
        toolbar.Children.Add(layoutLabel);

        var layoutMode = new ComboBox
        {
            Width = 150
        };
        layoutMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.CanvasLayoutModes)));
        layoutMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.CanvasLayoutMode), BindingMode.TwoWay));
        Grid.SetColumn(layoutMode, 5);
        toolbar.Children.Add(layoutMode);

        var refreshPreview = new Button
        {
            Content = "Refresh Preview"
        };
        refreshPreview.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.RefreshPreviewCommand)));
        Grid.SetColumn(refreshPreview, 6);
        toolbar.Children.Add(refreshPreview);

        var studioState = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = global::Avalonia.Media.TextAlignment.Right
        };
        studioState.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.StudioStateText)));
        Grid.SetColumn(studioState, 7);
        toolbar.Children.Add(studioState);

        grid.Children.Add(toolbar);

        var canvasPreviewPanel = BuildCanvasPreviewPanel();
        var canvasEditorPanel = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.SlateGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = CreateXamlEditor(
                nameof(XamlSourceGenStudioShellState.XamlText),
                nameof(XamlSourceGenStudioShellState.CanvasEditorDocumentUri),
                nameof(XamlSourceGenStudioShellState.CanvasEditorWorkspaceRoot))
        };

        var canvasSplitter = new GridSplitter
        {
            Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(64, 112, 128, 144))
        };
        var canvasWorkspace = new SourceGenStudioCanvasWorkspace(
            canvasPreviewPanel,
            canvasEditorPanel,
            canvasSplitter);
        canvasWorkspace.Bind(
            SourceGenStudioCanvasWorkspace.LayoutModeProperty,
            new Binding(nameof(XamlSourceGenStudioShellState.CanvasLayoutMode), BindingMode.TwoWay));

        Grid.SetRow(canvasWorkspace, 1);
        grid.Children.Add(canvasWorkspace);

        var status = new TextBlock
        {
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11,
            Foreground = global::Avalonia.Media.Brushes.Gray
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.StatusMessage)));
        Grid.SetRow(status, 2);
        grid.Children.Add(status);

        tab.Content = grid;
        return tab;
    }

    private Border BuildCanvasPreviewPanel()
    {
        var previewBorder = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.SlateGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };
        var previewGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 6
        };
        var previewHost = new ContentControl();
        previewHost.Bind(ContentControl.ContentProperty, new Binding(nameof(XamlSourceGenStudioShellState.CanvasPreviewContent)));
        previewGrid.Children.Add(new ScrollViewer
        {
            Content = previewHost
        });

        var previewStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = global::Avalonia.Media.Brushes.Gray
        };
        previewStatus.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.PreviewStatus)));
        Grid.SetRow(previewStatus, 1);
        previewGrid.Children.Add(previewStatus);

        previewBorder.Child = previewGrid;
        return previewBorder;
    }

    private TabItem BuildTemplateEditorTab()
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

        var templateSelector = new ComboBox
        {
            ItemTemplate = XamlSourceGenStudioViewTemplateFactory.CreateTextBlockTemplate<SourceGenHotDesignDocumentDescriptor>(
                static document => document.BuildUri)
        };
        templateSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.TemplateDocuments)));
        templateSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedTemplateDocument), BindingMode.TwoWay));
        grid.Children.Add(templateSelector);

        var templateEditorHost = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.SlateGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = CreateXamlEditor(
                nameof(XamlSourceGenStudioShellState.TemplateXamlText),
                nameof(XamlSourceGenStudioShellState.TemplateEditorDocumentUri),
                nameof(XamlSourceGenStudioShellState.TemplateEditorWorkspaceRoot))
        };
        Grid.SetRow(templateEditorHost, 1);
        grid.Children.Add(templateEditorHost);

        var applyTemplate = new Button
        {
            Content = "Apply Template",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        applyTemplate.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.ApplyTemplateXamlCommand)));
        Grid.SetRow(applyTemplate, 2);
        grid.Children.Add(applyTemplate);

        tab.Content = grid;
        return tab;
    }

    private TabItem BuildOperationsTab()
    {
        var tab = new TabItem
        {
            Header = "Operations"
        };

        var list = new ListBox();
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.StudioOperationLines)));
        tab.Content = list;
        return tab;
    }

    private static Control CreateXamlEditor(string textProperty, string documentUriProperty, string workspaceRootProperty)
    {
#if NET10_0_OR_GREATER
        var editor = new AxamlTextEditor
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            FontFamily = global::Avalonia.Media.FontFamily.Parse("Consolas")
        };
        editor.Bind(AxamlTextEditor.TextProperty, new Binding(textProperty, BindingMode.TwoWay));
        editor.Bind(AxamlTextEditor.DocumentUriProperty, new Binding(documentUriProperty));
        editor.Bind(AxamlTextEditor.WorkspaceRootProperty, new Binding(workspaceRootProperty));
        return editor;
#else
        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            FontFamily = global::Avalonia.Media.FontFamily.Parse("Consolas"),
            TextWrapping = global::Avalonia.Media.TextWrapping.NoWrap
        };
        editor.Bind(TextBox.TextProperty, new Binding(textProperty, BindingMode.TwoWay));
        return editor;
#endif
    }

    private static Control BuildPropertiesPanel()
    {
        var panel = new Border
        {
            BorderBrush = global::Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Properties",
            FontWeight = global::Avalonia.Media.FontWeight.Bold
        });

        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,120,120,Auto"),
            ColumnSpacing = 6
        };
        Grid.SetRow(filters, 1);

        var propertySearch = new TextBox
        {
            Watermark = "Filter properties"
        };
        propertySearch.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertySearchText), BindingMode.TwoWay));
        filters.Children.Add(propertySearch);

        var categoryFilter = new ComboBox
        {
            Width = 120
        };
        categoryFilter.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertyCategories)));
        categoryFilter.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedPropertyCategory), BindingMode.TwoWay));
        Grid.SetColumn(categoryFilter, 1);
        filters.Children.Add(categoryFilter);

        var sourceFilter = new ComboBox
        {
            Width = 120
        };
        sourceFilter.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertySources)));
        sourceFilter.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedPropertySource), BindingMode.TwoWay));
        Grid.SetColumn(sourceFilter, 2);
        filters.Children.Add(sourceFilter);

        var setOnly = new CheckBox
        {
            Content = "Set only",
            VerticalAlignment = VerticalAlignment.Center
        };
        setOnly.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(XamlSourceGenStudioShellState.ShowOnlySetProperties), BindingMode.TwoWay));
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
                            Foreground = property.IsPinned ? global::Avalonia.Media.Brushes.Goldenrod : global::Avalonia.Media.Brushes.Gray
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
                                    FontWeight = global::Avalonia.Media.FontWeight.SemiBold
                                },
                                new TextBlock
                                {
                                    Text = property.Category + " | " + property.EditorKind,
                                    FontSize = 11,
                                    Foreground = global::Avalonia.Media.Brushes.Gray
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = property.Value ?? string.Empty,
                            Foreground = global::Avalonia.Media.Brushes.Gray,
                            [Grid.ColumnProperty] = 2
                        },
                        new Border
                        {
                            Background = property.IsSet
                                ? new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(60, 86, 139, 255))
                                : new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(45, 120, 120, 120)),
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
        propertyList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.FilteredProperties)));
        propertyList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedProperty), BindingMode.TwoWay));
        Grid.SetRow(propertyList, 2);
        grid.Children.Add(propertyList);

        var propertyName = new TextBox
        {
            Watermark = "Property"
        };
        propertyName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertyName), BindingMode.TwoWay));
        Grid.SetRow(propertyName, 3);
        grid.Children.Add(propertyName);

        var propertyValue = new TextBox
        {
            Watermark = "Value"
        };
        propertyValue.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellState.PropertyValue), BindingMode.TwoWay));
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
        quickSetList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedPropertyQuickSets)));
        quickSetList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedQuickSet), BindingMode.TwoWay));
        Grid.SetRow(quickSetList, 5);
        grid.Children.Add(quickSetList);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        var quickSetApply = new Button
        {
            Content = "Apply Quick Set"
        };
        quickSetApply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.ApplyQuickSetCommand)));
        tools.Children.Add(quickSetApply);

        var pinProperty = new Button
        {
            Content = "Pin/Unpin Selected"
        };
        pinProperty.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.TogglePropertyPinCommand)));
        pinProperty.Bind(Button.CommandParameterProperty, new Binding(nameof(XamlSourceGenStudioShellState.SelectedProperty)));
        tools.Children.Add(pinProperty);
        Grid.SetRow(tools, 6);
        grid.Children.Add(tools);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        var applyProperty = new Button
        {
            Content = "Apply"
        };
        applyProperty.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.ApplyPropertyCommand)));
        actions.Children.Add(applyProperty);

        var removeProperty = new Button
        {
            Content = "Remove"
        };
        removeProperty.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellState.RemovePropertyCommand)));
        actions.Children.Add(removeProperty);

        Grid.SetRow(actions, 7);
        grid.Children.Add(actions);

        panel.Child = grid;
        return panel;
    }
}
