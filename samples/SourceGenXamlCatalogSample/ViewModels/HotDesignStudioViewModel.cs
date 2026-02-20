using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using SourceGenXamlCatalogSample.Infrastructure;
using XamlToCSharpGenerator.Runtime;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class HotDesignStudioViewModel : ViewModelBase
{
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _applyXamlCommand;
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly RelayCommand _insertElementCommand;
    private readonly RelayCommand _removeElementCommand;
    private readonly RelayCommand _applyPropertyCommand;
    private readonly RelayCommand _removePropertyCommand;
    private readonly RelayCommand _applyQuickSetCommand;
    private readonly RelayCommand _togglePanelCommand;

    private SourceGenHotDesignDocumentDescriptor? _selectedDocument;
    private SourceGenHotDesignElementNode? _selectedElement;
    private SourceGenHotDesignPropertyEntry? _selectedProperty;
    private SourceGenHotDesignToolboxItem? _selectedToolboxItem;
    private string _searchText = string.Empty;
    private string _statusMessage = "Ready.";
    private string _xamlText = string.Empty;
    private string _activeBuildUri = string.Empty;
    private string _selectedElementId = "0";
    private string _propertyName = string.Empty;
    private string _propertyValue = string.Empty;
    private string _newElementName = "Button";
    private string _formFactor = "Desktop";
    private double _zoom = 1.0;
    private bool _canUndo;
    private bool _canRedo;
    private bool _isRefreshing;
    private SourceGenHotDesignWorkspaceMode _workspaceMode = SourceGenHotDesignWorkspaceMode.Design;
    private SourceGenHotDesignPropertyFilterMode _propertyFilterMode = SourceGenHotDesignPropertyFilterMode.Smart;

    public HotDesignStudioViewModel()
    {
        WorkspaceModes = Enum.GetValues<SourceGenHotDesignWorkspaceMode>();
        PropertyFilterModes = Enum.GetValues<SourceGenHotDesignPropertyFilterMode>();

        _refreshCommand = new RelayCommand(RefreshSnapshot);
        _applyXamlCommand = new RelayCommand(async _ => await ApplyXamlAsync());
        _undoCommand = new RelayCommand(async _ => await UndoAsync(), _ => CanUndo);
        _redoCommand = new RelayCommand(async _ => await RedoAsync(), _ => CanRedo);
        _insertElementCommand = new RelayCommand(async _ => await InsertElementAsync());
        _removeElementCommand = new RelayCommand(async _ => await RemoveElementAsync());
        _applyPropertyCommand = new RelayCommand(async _ => await ApplyPropertyAsync());
        _removePropertyCommand = new RelayCommand(async _ => await RemovePropertyAsync());
        _applyQuickSetCommand = new RelayCommand(async value => await ApplyQuickSetAsync(value as string));
        _togglePanelCommand = new RelayCommand(value => TogglePanel(value as string));

        XamlSourceGenHotDesignManager.HotDesignUpdateApplied += OnHotDesignUpdateApplied;
        XamlSourceGenHotReloadManager.HotReloadPipelineCompleted += OnHotReloadCompleted;

        RefreshSnapshot();
    }

    public ObservableCollection<SourceGenHotDesignDocumentDescriptor> Documents { get; } = [];

    public ObservableCollection<SourceGenHotDesignElementNode> Elements { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyEntry> Properties { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyQuickSet> SelectedPropertyQuickSets { get; } = [];

    public ObservableCollection<SourceGenHotDesignToolboxCategory> ToolboxCategories { get; } = [];

    public IReadOnlyList<SourceGenHotDesignWorkspaceMode> WorkspaceModes { get; }

    public IReadOnlyList<SourceGenHotDesignPropertyFilterMode> PropertyFilterModes { get; }

    public RelayCommand RefreshCommand => _refreshCommand;

    public RelayCommand ApplyXamlCommand => _applyXamlCommand;

    public RelayCommand UndoCommand => _undoCommand;

    public RelayCommand RedoCommand => _redoCommand;

    public RelayCommand InsertElementCommand => _insertElementCommand;

    public RelayCommand RemoveElementCommand => _removeElementCommand;

    public RelayCommand ApplyPropertyCommand => _applyPropertyCommand;

    public RelayCommand RemovePropertyCommand => _removePropertyCommand;

    public RelayCommand ApplyQuickSetCommand => _applyQuickSetCommand;

    public RelayCommand TogglePanelCommand => _togglePanelCommand;

    public SourceGenHotDesignDocumentDescriptor? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!SetProperty(ref _selectedDocument, value) || value is null || _isRefreshing)
            {
                return;
            }

            ActiveBuildUri = value.BuildUri;
        }
    }

    public SourceGenHotDesignElementNode? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (!SetProperty(ref _selectedElement, value) || value is null || _isRefreshing)
            {
                return;
            }

            SelectElement(value.Id);
        }
    }

    public SourceGenHotDesignPropertyEntry? SelectedProperty
    {
        get => _selectedProperty;
        set
        {
            if (!SetProperty(ref _selectedProperty, value))
            {
                return;
            }

            SelectedPropertyQuickSets.Clear();
            if (value is null)
            {
                PropertyName = string.Empty;
                PropertyValue = string.Empty;
                return;
            }

            PropertyName = value.Name;
            PropertyValue = value.Value ?? string.Empty;
            for (var index = 0; index < value.QuickSets.Count; index++)
            {
                SelectedPropertyQuickSets.Add(value.QuickSets[index]);
            }
        }
    }

    public SourceGenHotDesignToolboxItem? SelectedToolboxItem
    {
        get => _selectedToolboxItem;
        set
        {
            if (!SetProperty(ref _selectedToolboxItem, value) || value is null)
            {
                return;
            }

            NewElementName = value.Name;
        }
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string XamlText
    {
        get => _xamlText;
        set => SetProperty(ref _xamlText, value);
    }

    public string ActiveBuildUri
    {
        get => _activeBuildUri;
        set
        {
            if (!SetProperty(ref _activeBuildUri, value) || _isRefreshing)
            {
                return;
            }

            XamlSourceGenHotDesignTool.SelectDocument(value);
            RefreshSnapshot();
        }
    }

    public string SelectedElementId
    {
        get => _selectedElementId;
        set => SetProperty(ref _selectedElementId, value);
    }

    public string PropertyName
    {
        get => _propertyName;
        set => SetProperty(ref _propertyName, value);
    }

    public string PropertyValue
    {
        get => _propertyValue;
        set => SetProperty(ref _propertyValue, value);
    }

    public string NewElementName
    {
        get => _newElementName;
        set => SetProperty(ref _newElementName, value);
    }

    public string FormFactor
    {
        get => _formFactor;
        set
        {
            if (!SetProperty(ref _formFactor, value) || _isRefreshing)
            {
                return;
            }

            XamlSourceGenHotDesignTool.SetCanvasFormFactor(value);
            RefreshSnapshot();
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!SetProperty(ref _zoom, value) || _isRefreshing)
            {
                return;
            }

            XamlSourceGenHotDesignTool.SetCanvasZoom(value);
        }
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set
        {
            if (!SetProperty(ref _canUndo, value))
            {
                return;
            }

            _undoCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanRedo
    {
        get => _canRedo;
        private set
        {
            if (!SetProperty(ref _canRedo, value))
            {
                return;
            }

            _redoCommand.NotifyCanExecuteChanged();
        }
    }

    public SourceGenHotDesignWorkspaceMode WorkspaceMode
    {
        get => _workspaceMode;
        set
        {
            if (!SetProperty(ref _workspaceMode, value) || _isRefreshing)
            {
                return;
            }

            XamlSourceGenHotDesignTool.SetWorkspaceMode(value);
            RefreshSnapshot();
        }
    }

    public SourceGenHotDesignPropertyFilterMode PropertyFilterMode
    {
        get => _propertyFilterMode;
        set
        {
            if (!SetProperty(ref _propertyFilterMode, value) || _isRefreshing)
            {
                return;
            }

            XamlSourceGenHotDesignTool.SetPropertyFilterMode(value);
            RefreshSnapshot();
        }
    }

    public void SelectElement(string? elementId)
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri) || string.IsNullOrWhiteSpace(elementId))
        {
            return;
        }

        SelectedElementId = elementId;
        XamlSourceGenHotDesignTool.SelectElement(ActiveBuildUri, elementId);
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        var snapshot = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(
            string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri,
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);

        _isRefreshing = true;
        try
        {
            ReplaceCollection(Documents, snapshot.Documents);
            ReplaceCollection(Elements, snapshot.Elements);
            ReplaceCollection(Properties, snapshot.Properties);
            ReplaceCollection(ToolboxCategories, snapshot.Toolbox);

            ActiveBuildUri = snapshot.ActiveBuildUri ?? string.Empty;
            SelectedElementId = snapshot.SelectedElementId ?? "0";
            XamlText = snapshot.CurrentXamlText ?? string.Empty;
            WorkspaceMode = snapshot.Mode;
            PropertyFilterMode = snapshot.PropertyFilterMode;
            Zoom = snapshot.Canvas.Zoom;
            FormFactor = snapshot.Canvas.FormFactor;
            CanUndo = snapshot.CanUndo;
            CanRedo = snapshot.CanRedo;

            SelectedDocument = Documents.FirstOrDefault(document =>
                string.Equals(document.BuildUri, snapshot.ActiveBuildUri, StringComparison.OrdinalIgnoreCase));

            SelectedElement = FindById(snapshot.Elements, snapshot.SelectedElementId);
            SelectedProperty = Properties.FirstOrDefault(property =>
                string.Equals(property.Name, PropertyName, StringComparison.OrdinalIgnoreCase));

            StatusMessage = snapshot.Status.IsEnabled
                ? "Hot Design active. Documents=" + snapshot.Documents.Count + ", Elements=" + snapshot.Elements.Count + "."
                : "Hot Design is currently disabled.";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async System.Threading.Tasks.Task ApplyXamlAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri))
        {
            StatusMessage = "Select a document first.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.ApplyDocumentTextAsync(ActiveBuildUri, XamlText);
        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task UndoAsync()
    {
        var result = await XamlSourceGenHotDesignTool.UndoAsync(string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri);
        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task RedoAsync()
    {
        var result = await XamlSourceGenHotDesignTool.RedoAsync(string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri);
        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task InsertElementAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri))
        {
            StatusMessage = "Select a document first.";
            return;
        }

        var elementName = !string.IsNullOrWhiteSpace(NewElementName)
            ? NewElementName
            : SelectedToolboxItem?.Name;

        if (string.IsNullOrWhiteSpace(elementName))
        {
            StatusMessage = "Select toolbox item or provide element name.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.InsertElementAsync(new SourceGenHotDesignElementInsertRequest
        {
            BuildUri = ActiveBuildUri,
            ParentElementId = SelectedElementId,
            ElementName = elementName,
            WaitForHotReload = false,
            PersistChangesToSource = true
        });

        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task RemoveElementAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri))
        {
            StatusMessage = "Select a document first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedElementId) || string.Equals(SelectedElementId, "0", StringComparison.Ordinal))
        {
            StatusMessage = "Select a non-root element to remove.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.RemoveElementAsync(new SourceGenHotDesignElementRemoveRequest
        {
            BuildUri = ActiveBuildUri,
            ElementId = SelectedElementId,
            WaitForHotReload = false,
            PersistChangesToSource = true
        });

        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task ApplyPropertyAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri))
        {
            StatusMessage = "Select a document first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(PropertyName))
        {
            StatusMessage = "Property name is required.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.ApplyPropertyUpdateAsync(new SourceGenHotDesignPropertyUpdateRequest
        {
            BuildUri = ActiveBuildUri,
            ElementId = SelectedElementId,
            PropertyName = PropertyName,
            PropertyValue = PropertyValue,
            WaitForHotReload = false,
            PersistChangesToSource = true
        });

        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task RemovePropertyAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri) || string.IsNullOrWhiteSpace(PropertyName))
        {
            StatusMessage = "Select document and property first.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.ApplyPropertyUpdateAsync(new SourceGenHotDesignPropertyUpdateRequest
        {
            BuildUri = ActiveBuildUri,
            ElementId = SelectedElementId,
            PropertyName = PropertyName,
            RemoveProperty = true,
            WaitForHotReload = false,
            PersistChangesToSource = true
        });

        StatusMessage = result.Message;
        RefreshSnapshot();
    }

    private async System.Threading.Tasks.Task ApplyQuickSetAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        PropertyValue = value;
        await ApplyPropertyAsync();
    }

    private void TogglePanel(string? panelName)
    {
        if (string.IsNullOrWhiteSpace(panelName) ||
            !Enum.TryParse<SourceGenHotDesignPanelKind>(panelName, ignoreCase: true, out var panel))
        {
            return;
        }

        XamlSourceGenHotDesignTool.TogglePanel(panel);
        RefreshSnapshot();
    }

    private static SourceGenHotDesignElementNode? FindById(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (string.Equals(node.Id, id, StringComparison.Ordinal))
            {
                return node;
            }

            var found = FindById(node.Children, id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private void OnHotDesignUpdateApplied(SourceGenHotDesignApplyResult result)
    {
        Dispatcher.UIThread.Post(RefreshSnapshot);
    }

    private void OnHotReloadCompleted(SourceGenHotReloadUpdateContext context)
    {
        Dispatcher.UIThread.Post(RefreshSnapshot);
    }
}
