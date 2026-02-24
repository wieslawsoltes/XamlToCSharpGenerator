using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioShellView : UserControl
{
    public XamlSourceGenStudioShellView()
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
            BorderBrush = Avalonia.Media.Brushes.SteelBlue,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("240,130,130,130,260,*,Auto,Auto,Auto,Auto"),
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
            ItemTemplate = new FuncDataTemplate<SourceGenStudioScopeDescriptor>(
                (scope, _) => new TextBlock
                {
                    Text = scope.ScopeKind + ": " + scope.DisplayName
                },
                supportsRecycling: true)
        };
        scopeSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Scopes)));
        scopeSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedScope), BindingMode.TwoWay));
        grid.Children.Add(scopeSelector);

        var workspaceMode = new ComboBox
        {
            Width = 130
        };
        workspaceMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.WorkspaceModes)));
        workspaceMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.WorkspaceMode), BindingMode.TwoWay));
        Grid.SetColumn(workspaceMode, 1);
        grid.Children.Add(workspaceMode);

        var propertyMode = new ComboBox
        {
            Width = 130
        };
        propertyMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyFilterModes)));
        propertyMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyFilterMode), BindingMode.TwoWay));
        Grid.SetColumn(propertyMode, 2);
        grid.Children.Add(propertyMode);

        var hitTestMode = new ComboBox
        {
            Width = 130
        };
        hitTestMode.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.HitTestModes)));
        hitTestMode.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.HitTestMode), BindingMode.TwoWay));
        Grid.SetColumn(hitTestMode, 3);
        grid.Children.Add(hitTestMode);

        var documentSelector = new ComboBox
        {
            Width = 260,
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignDocumentDescriptor>(
                (document, _) => new TextBlock
                {
                    Text = document.BuildUri
                },
                supportsRecycling: true)
        };
        documentSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Documents)));
        documentSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedDocument), BindingMode.TwoWay));
        Grid.SetColumn(documentSelector, 4);
        grid.Children.Add(documentSelector);

        var searchText = new TextBox
        {
            Watermark = "Search elements/toolbox"
        };
        searchText.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SearchText), BindingMode.TwoWay));
        Grid.SetColumn(searchText, 5);
        grid.Children.Add(searchText);

        var refreshButton = new Button
        {
            Content = "Refresh",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        refreshButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RefreshCommand)));
        Grid.SetColumn(refreshButton, 6);
        grid.Children.Add(refreshButton);

        var undoButton = new Button
        {
            Content = "Undo",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        undoButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.UndoCommand)));
        Grid.SetColumn(undoButton, 7);
        grid.Children.Add(undoButton);

        var redoButton = new Button
        {
            Content = "Redo",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        redoButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RedoCommand)));
        Grid.SetColumn(redoButton, 8);
        grid.Children.Add(redoButton);

        var applyButton = new Button
        {
            Content = "Apply XAML",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        applyButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyXamlCommand)));
        Grid.SetColumn(applyButton, 9);
        grid.Children.Add(applyButton);

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
            BorderBrush = Avalonia.Media.Brushes.DimGray,
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
            FontWeight = Avalonia.Media.FontWeight.Bold
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
                                FontWeight = item.IsSelected ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal
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
        tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Elements)));
        tree.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedElement), BindingMode.TwoWay));
        Grid.SetRow(tree, 1);
        elementsGrid.Children.Add(tree);

        var removeElementButton = new Button
        {
            Content = "Remove Selected",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        removeElementButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RemoveElementCommand)));
        Grid.SetRow(removeElementButton, 2);
        elementsGrid.Children.Add(removeElementButton);
        elementsPanel.Child = elementsGrid;
        left.Children.Add(elementsPanel);

        var toolboxPanel = new Border
        {
            BorderBrush = Avalonia.Media.Brushes.DimGray,
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
            FontWeight = Avalonia.Media.FontWeight.Bold
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
                            Foreground = Avalonia.Media.Brushes.Gray
                        }
                    }
                },
                supportsRecycling: true)
        };
        toolboxList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ToolboxItems)));
        toolboxList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedToolboxItem), BindingMode.TwoWay));
        Grid.SetRow(toolboxList, 1);
        toolboxGrid.Children.Add(toolboxList);

        var elementName = new TextBox
        {
            Watermark = "Element name"
        };
        elementName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.NewElementName), BindingMode.TwoWay));
        Grid.SetRow(elementName, 2);
        toolboxGrid.Children.Add(elementName);

        var insertButton = new Button
        {
            Content = "Insert Under Selection",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        insertButton.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.InsertElementCommand)));
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
            BorderBrush = Avalonia.Media.Brushes.DimGray,
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
            RowDefinitions = new RowDefinitions("Auto,2*,3*,Auto"),
            RowSpacing = 8
        };

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,Auto,110,Auto,*"),
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
        zoom.Bind(RangeBase.ValueProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.Zoom), BindingMode.TwoWay));
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
        formFactor.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.FormFactor), BindingMode.TwoWay));
        Grid.SetColumn(formFactor, 3);
        toolbar.Children.Add(formFactor);

        var refreshPreview = new Button
        {
            Content = "Refresh Preview"
        };
        refreshPreview.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RefreshPreviewCommand)));
        Grid.SetColumn(refreshPreview, 4);
        toolbar.Children.Add(refreshPreview);

        var studioState = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Right
        };
        studioState.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StudioStateText)));
        Grid.SetColumn(studioState, 5);
        toolbar.Children.Add(studioState);

        grid.Children.Add(toolbar);

        var previewBorder = new Border
        {
            BorderBrush = Avalonia.Media.Brushes.SlateGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };
        var previewGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 6
        };
        var previewHost = new ContentControl();
        previewHost.Bind(ContentControl.ContentProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.CanvasPreviewContent)));
        previewGrid.Children.Add(new ScrollViewer
        {
            Content = previewHost
        });

        var previewStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.Gray
        };
        previewStatus.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PreviewStatus)));
        Grid.SetRow(previewStatus, 1);
        previewGrid.Children.Add(previewStatus);

        previewBorder.Child = previewGrid;
        Grid.SetRow(previewBorder, 1);
        grid.Children.Add(previewBorder);

        var xamlEditor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            FontFamily = Avalonia.Media.FontFamily.Parse("Consolas"),
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
        };
        xamlEditor.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.XamlText), BindingMode.TwoWay));
        Grid.SetRow(xamlEditor, 2);
        grid.Children.Add(xamlEditor);

        var status = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.Gray
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StatusMessage)));
        Grid.SetRow(status, 3);
        grid.Children.Add(status);

        tab.Content = grid;
        return tab;
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
            ItemTemplate = new FuncDataTemplate<SourceGenHotDesignDocumentDescriptor>(
                (document, _) => new TextBlock
                {
                    Text = document.BuildUri
                },
                supportsRecycling: true)
        };
        templateSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.TemplateDocuments)));
        templateSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedTemplateDocument), BindingMode.TwoWay));
        grid.Children.Add(templateSelector);

        var templateEditor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            FontFamily = Avalonia.Media.FontFamily.Parse("Consolas"),
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
        };
        templateEditor.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.TemplateXamlText), BindingMode.TwoWay));
        Grid.SetRow(templateEditor, 1);
        grid.Children.Add(templateEditor);

        var applyTemplate = new Button
        {
            Content = "Apply Template",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        applyTemplate.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyTemplateXamlCommand)));
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
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.StudioOperationLines)));
        tab.Content = list;
        return tab;
    }

    private static Control BuildPropertiesPanel()
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
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Properties",
            FontWeight = Avalonia.Media.FontWeight.Bold
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
                            Foreground = property.IsPinned ? Avalonia.Media.Brushes.Goldenrod : Avalonia.Media.Brushes.Gray
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
                                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                                },
                                new TextBlock
                                {
                                    Text = property.Category + " | " + property.EditorKind,
                                    FontSize = 11,
                                    Foreground = Avalonia.Media.Brushes.Gray
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = property.Value ?? string.Empty,
                            Foreground = Avalonia.Media.Brushes.Gray,
                            [Grid.ColumnProperty] = 2
                        },
                        new Border
                        {
                            Background = property.IsSet
                                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(60, 86, 139, 255))
                                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(45, 120, 120, 120)),
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

        var propertyName = new TextBox
        {
            Watermark = "Property"
        };
        propertyName.Bind(TextBox.TextProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.PropertyName), BindingMode.TwoWay));
        Grid.SetRow(propertyName, 3);
        grid.Children.Add(propertyName);

        var propertyValue = new TextBox
        {
            Watermark = "Value"
        };
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

        var quickSetApply = new Button
        {
            Content = "Apply Quick Set"
        };
        quickSetApply.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyQuickSetCommand)));
        tools.Children.Add(quickSetApply);

        var pinProperty = new Button
        {
            Content = "Pin/Unpin Selected"
        };
        pinProperty.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.TogglePropertyPinCommand)));
        pinProperty.Bind(Button.CommandParameterProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.SelectedProperty)));
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
        applyProperty.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.ApplyPropertyCommand)));
        actions.Children.Add(applyProperty);

        var removeProperty = new Button
        {
            Content = "Remove"
        };
        removeProperty.Bind(Button.CommandProperty, new Binding(nameof(XamlSourceGenStudioShellViewModel.RemovePropertyCommand)));
        actions.Children.Add(removeProperty);

        Grid.SetRow(actions, 7);
        grid.Children.Add(actions);

        panel.Child = grid;
        return panel;
    }
}
