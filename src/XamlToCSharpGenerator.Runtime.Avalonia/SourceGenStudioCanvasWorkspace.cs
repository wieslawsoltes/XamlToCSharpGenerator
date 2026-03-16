using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Layout;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class SourceGenStudioCanvasWorkspace : Grid
{
    public static readonly DirectProperty<SourceGenStudioCanvasWorkspace, SourceGenStudioCanvasLayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<SourceGenStudioCanvasWorkspace, SourceGenStudioCanvasLayoutMode>(
            nameof(LayoutMode),
            workspace => workspace.LayoutMode,
            (workspace, value) => workspace.LayoutMode = value);

    private readonly Control _previewPanel;
    private readonly Control _editorPanel;
    private readonly GridSplitter _splitter;
    private SourceGenStudioCanvasLayoutMode _layoutMode = SourceGenStudioCanvasLayoutMode.SideBySide;

    public SourceGenStudioCanvasWorkspace(
        Control previewPanel,
        Control editorPanel,
        GridSplitter splitter)
    {
        _previewPanel = previewPanel ?? throw new ArgumentNullException(nameof(previewPanel));
        _editorPanel = editorPanel ?? throw new ArgumentNullException(nameof(editorPanel));
        _splitter = splitter ?? throw new ArgumentNullException(nameof(splitter));

        Children.Add(_previewPanel);
        Children.Add(_editorPanel);
        Children.Add(_splitter);

        ApplyLayout();
    }

    public SourceGenStudioCanvasLayoutMode LayoutMode
    {
        get => _layoutMode;
        set
        {
            if (_layoutMode == value)
            {
                return;
            }

            SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
            ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        var showsPreview = _layoutMode != SourceGenStudioCanvasLayoutMode.EditorOnly;
        var showsEditor = _layoutMode != SourceGenStudioCanvasLayoutMode.PreviewOnly;
        var isSplit = showsPreview && showsEditor;

        _previewPanel.IsVisible = showsPreview;
        _editorPanel.IsVisible = showsEditor;
        _splitter.IsVisible = isSplit;

        switch (_layoutMode)
        {
            case SourceGenStudioCanvasLayoutMode.Stacked:
                ColumnDefinitions = new ColumnDefinitions("*");
                RowDefinitions = new RowDefinitions("*,6,*");
                ConfigureSplitterForRows(_splitter);
                SetGridPosition(_previewPanel, row: 0, column: 0);
                SetGridPosition(_splitter, row: 1, column: 0);
                SetGridPosition(_editorPanel, row: 2, column: 0);
                break;

            case SourceGenStudioCanvasLayoutMode.PreviewOnly:
            case SourceGenStudioCanvasLayoutMode.EditorOnly:
                ColumnDefinitions = new ColumnDefinitions("*");
                RowDefinitions = new RowDefinitions("*");
                SetGridPosition(_previewPanel, row: 0, column: 0);
                SetGridPosition(_splitter, row: 0, column: 0);
                SetGridPosition(_editorPanel, row: 0, column: 0);
                break;

            default:
                ColumnDefinitions = new ColumnDefinitions("*,6,*");
                RowDefinitions = new RowDefinitions("*");
                ConfigureSplitterForColumns(_splitter);
                SetGridPosition(_previewPanel, row: 0, column: 0);
                SetGridPosition(_splitter, row: 0, column: 1);
                SetGridPosition(_editorPanel, row: 0, column: 2);
                break;
        }
    }

    private static void ConfigureSplitterForColumns(GridSplitter splitter)
    {
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.Width = 6;
        splitter.Height = double.NaN;
        splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private static void ConfigureSplitterForRows(GridSplitter splitter)
    {
        splitter.ResizeDirection = GridResizeDirection.Rows;
        splitter.Width = double.NaN;
        splitter.Height = 6;
        splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private static void SetGridPosition(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetRowSpan(control, 1);
        Grid.SetColumnSpan(control, 1);
    }
}
