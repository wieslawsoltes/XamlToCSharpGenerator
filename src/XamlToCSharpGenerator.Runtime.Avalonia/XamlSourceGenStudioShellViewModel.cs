using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly StudioRelayCommand _refreshCommand;
    private readonly StudioRelayCommand _refreshPreviewCommand;
    private readonly StudioRelayCommand _applyXamlCommand;
    private readonly StudioRelayCommand _applyTemplateXamlCommand;
    private readonly StudioRelayCommand _undoCommand;
    private readonly StudioRelayCommand _redoCommand;
    private readonly StudioRelayCommand _insertElementCommand;
    private readonly StudioRelayCommand _removeElementCommand;
    private readonly StudioRelayCommand _applyPropertyCommand;
    private readonly StudioRelayCommand _removePropertyCommand;
    private readonly StudioRelayCommand _applyQuickSetCommand;
    private readonly StudioRelayCommand _togglePanelCommand;

    private SourceGenStudioScopeDescriptor? _selectedScope;
    private SourceGenHotDesignDocumentDescriptor? _selectedDocument;
    private SourceGenHotDesignDocumentDescriptor? _selectedTemplateDocument;
    private SourceGenHotDesignElementNode? _selectedElement;
    private SourceGenHotDesignPropertyEntry? _selectedProperty;
    private SourceGenHotDesignPropertyQuickSet? _selectedQuickSet;
    private SourceGenHotDesignToolboxItem? _selectedToolboxItem;
    private string _searchText = string.Empty;
    private string _statusMessage = "Ready.";
    private string _studioStateText = "Ready";
    private string _previewStatus = "Preview unavailable.";
    private string _xamlText = string.Empty;
    private string _templateXamlText = string.Empty;
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
    private bool _isDisposed;
    private object? _canvasPreviewContent;
    private SourceGenHotDesignWorkspaceMode _workspaceMode = SourceGenHotDesignWorkspaceMode.Design;
    private SourceGenHotDesignPropertyFilterMode _propertyFilterMode = SourceGenHotDesignPropertyFilterMode.Smart;

    public XamlSourceGenStudioShellViewModel(SourceGenStudioOptions options)
    {
        WorkspaceModes = Enum.GetValues<SourceGenHotDesignWorkspaceMode>();
        PropertyFilterModes = Enum.GetValues<SourceGenHotDesignPropertyFilterMode>();

        _refreshCommand = new StudioRelayCommand(_ => RefreshAll());
        _refreshPreviewCommand = new StudioRelayCommand(_ => RefreshCanvasPreview());
        _applyXamlCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(ApplyXamlAsync));
        _applyTemplateXamlCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(ApplyTemplateXamlAsync));
        _undoCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(UndoAsync), _ => CanUndo);
        _redoCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(RedoAsync), _ => CanRedo);
        _insertElementCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(InsertElementAsync));
        _removeElementCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(RemoveElementAsync));
        _applyPropertyCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(ApplyPropertyAsync));
        _removePropertyCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(RemovePropertyAsync));
        _applyQuickSetCommand = new StudioRelayCommand(_ => _ = RunCommandAsync(ApplyQuickSetAsync));
        _togglePanelCommand = new StudioRelayCommand(value => TogglePanel(value as string));

        XamlSourceGenHotDesignManager.HotDesignUpdateApplied += OnHotDesignUpdateApplied;
        XamlSourceGenHotReloadManager.HotReloadPipelineCompleted += OnHotReloadCompleted;
        XamlSourceGenStudioManager.StudioStatusChanged += OnStudioStatusChanged;

        RefreshAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SourceGenStudioScopeDescriptor> Scopes { get; } = [];

    public ObservableCollection<SourceGenHotDesignDocumentDescriptor> Documents { get; } = [];

    public ObservableCollection<SourceGenHotDesignDocumentDescriptor> TemplateDocuments { get; } = [];

    public ObservableCollection<SourceGenHotDesignElementNode> Elements { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyEntry> Properties { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyQuickSet> SelectedPropertyQuickSets { get; } = [];

    public ObservableCollection<SourceGenHotDesignToolboxItem> ToolboxItems { get; } = [];

    public ObservableCollection<string> StudioOperationLines { get; } = [];

    public IReadOnlyList<SourceGenHotDesignWorkspaceMode> WorkspaceModes { get; }

    public IReadOnlyList<SourceGenHotDesignPropertyFilterMode> PropertyFilterModes { get; }

    public StudioRelayCommand RefreshCommand => _refreshCommand;

    public StudioRelayCommand RefreshPreviewCommand => _refreshPreviewCommand;

    public StudioRelayCommand ApplyXamlCommand => _applyXamlCommand;

    public StudioRelayCommand ApplyTemplateXamlCommand => _applyTemplateXamlCommand;

    public StudioRelayCommand UndoCommand => _undoCommand;

    public StudioRelayCommand RedoCommand => _redoCommand;

    public StudioRelayCommand InsertElementCommand => _insertElementCommand;

    public StudioRelayCommand RemoveElementCommand => _removeElementCommand;

    public StudioRelayCommand ApplyPropertyCommand => _applyPropertyCommand;

    public StudioRelayCommand RemovePropertyCommand => _removePropertyCommand;

    public StudioRelayCommand ApplyQuickSetCommand => _applyQuickSetCommand;

    public StudioRelayCommand TogglePanelCommand => _togglePanelCommand;

    public SourceGenStudioScopeDescriptor? SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (!SetProperty(ref _selectedScope, value) || value?.BuildUri is not { Length: > 0 } buildUri || _isRefreshing)
            {
                return;
            }

            ActiveBuildUri = buildUri;
        }
    }

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

    public SourceGenHotDesignDocumentDescriptor? SelectedTemplateDocument
    {
        get => _selectedTemplateDocument;
        set
        {
            if (!SetProperty(ref _selectedTemplateDocument, value) || value is null || _isRefreshing)
            {
                return;
            }

            ActiveBuildUri = value.BuildUri;
            TemplateXamlText = XamlText;
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
            SelectedQuickSet = null;

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

    public SourceGenHotDesignPropertyQuickSet? SelectedQuickSet
    {
        get => _selectedQuickSet;
        set => SetProperty(ref _selectedQuickSet, value);
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

    public string StudioStateText
    {
        get => _studioStateText;
        set => SetProperty(ref _studioStateText, value);
    }

    public string PreviewStatus
    {
        get => _previewStatus;
        set => SetProperty(ref _previewStatus, value);
    }

    public string XamlText
    {
        get => _xamlText;
        set => SetProperty(ref _xamlText, value);
    }

    public string TemplateXamlText
    {
        get => _templateXamlText;
        set => SetProperty(ref _templateXamlText, value);
    }

    public object? CanvasPreviewContent
    {
        get => _canvasPreviewContent;
        set => SetProperty(ref _canvasPreviewContent, value);
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
            RefreshAll();
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
            RefreshWorkspace();
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
            RefreshWorkspace();
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
            RefreshWorkspace();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        XamlSourceGenHotDesignManager.HotDesignUpdateApplied -= OnHotDesignUpdateApplied;
        XamlSourceGenHotReloadManager.HotReloadPipelineCompleted -= OnHotReloadCompleted;
        XamlSourceGenStudioManager.StudioStatusChanged -= OnStudioStatusChanged;
    }

    public void RefreshAll()
    {
        RefreshStudioStatus();
        RefreshWorkspace();
        RefreshCanvasPreview();
    }

    public void SelectElement(string? elementId)
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri) || string.IsNullOrWhiteSpace(elementId))
        {
            return;
        }

        SelectedElementId = elementId;
        XamlSourceGenHotDesignTool.SelectElement(ActiveBuildUri, elementId);
        RefreshWorkspace();
    }

    private void RefreshWorkspace()
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
            ReplaceCollection(ToolboxItems, FlattenToolbox(snapshot.Toolbox));

            var templates = snapshot.Documents.Where(static document =>
                document.ArtifactKind == SourceGenHotDesignArtifactKind.Template ||
                document.DocumentRole == SourceGenHotDesignDocumentRole.Template).ToArray();
            ReplaceCollection(TemplateDocuments, templates);

            ActiveBuildUri = snapshot.ActiveBuildUri ?? string.Empty;
            SelectedElementId = snapshot.SelectedElementId ?? "0";
            XamlText = snapshot.CurrentXamlText ?? string.Empty;
            WorkspaceMode = snapshot.Mode;
            PropertyFilterMode = snapshot.PropertyFilterMode;
            Zoom = snapshot.Canvas.Zoom;
            FormFactor = snapshot.Canvas.FormFactor;
            CanUndo = snapshot.CanUndo;
            CanRedo = snapshot.CanRedo;

            var selectedDocument = Documents.FirstOrDefault(document =>
                string.Equals(document.BuildUri, snapshot.ActiveBuildUri, StringComparison.OrdinalIgnoreCase));
            _selectedDocument = selectedDocument;
            OnPropertyChanged(nameof(SelectedDocument));

            var selectedTemplateDocument = TemplateDocuments.FirstOrDefault(document =>
                string.Equals(document.BuildUri, snapshot.ActiveBuildUri, StringComparison.OrdinalIgnoreCase));
            if (selectedTemplateDocument is null && TemplateDocuments.Count > 0)
            {
                selectedTemplateDocument = TemplateDocuments[0];
            }

            _selectedTemplateDocument = selectedTemplateDocument;
            OnPropertyChanged(nameof(SelectedTemplateDocument));
            TemplateXamlText = selectedTemplateDocument is null ? string.Empty : XamlText;

            var selectedElement = FindById(snapshot.Elements, snapshot.SelectedElementId);
            _selectedElement = selectedElement;
            OnPropertyChanged(nameof(SelectedElement));

            var selectedProperty = Properties.FirstOrDefault(property =>
                string.Equals(property.Name, PropertyName, StringComparison.OrdinalIgnoreCase));
            _selectedProperty = selectedProperty;
            OnPropertyChanged(nameof(SelectedProperty));

            StatusMessage = snapshot.Status.IsEnabled
                ? "Hot Design active. Documents=" + snapshot.Documents.Count + ", Elements=" + snapshot.Elements.Count + "."
                : "Hot Design is currently disabled.";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshStudioStatus()
    {
        var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();

        ReplaceCollection(Scopes, snapshot.Scopes);

        var scope = Scopes.FirstOrDefault(candidate =>
            string.Equals(candidate.BuildUri, ActiveBuildUri, StringComparison.OrdinalIgnoreCase));
        if (scope is null && Scopes.Count > 0)
        {
            scope = Scopes[0];
        }

        _selectedScope = scope;
        OnPropertyChanged(nameof(SelectedScope));

        var operationLines = snapshot.Operations
            .OrderByDescending(static operation => operation.OperationId)
            .Select(operation =>
            {
                var message = operation.Result?.Message ?? "Running";
                var requestId = string.IsNullOrWhiteSpace(operation.RequestId) ? "-" : operation.RequestId;
                return "#" + operation.OperationId + " [" + operation.State + "] " + requestId + " - " + message;
            })
            .ToArray();
        ReplaceCollection(StudioOperationLines, operationLines);

        var session = snapshot.SessionId == Guid.Empty ? "<none>" : snapshot.SessionId.ToString("N")[..8];
        StudioStateText = "Studio " + (snapshot.IsEnabled ? "On" : "Off") + " | State=" + snapshot.CurrentState + " | Session=" + session;
    }

    private void RefreshCanvasPreview()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri))
        {
            CanvasPreviewContent = BuildInfoPreview("Select a document to preview.");
            PreviewStatus = "Preview unavailable.";
            return;
        }

        var document = Documents.FirstOrDefault(candidate =>
            string.Equals(candidate.BuildUri, ActiveBuildUri, StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            CanvasPreviewContent = BuildInfoPreview("Selected document metadata is unavailable.");
            PreviewStatus = "Preview unavailable.";
            return;
        }

        if (document.ArtifactKind is SourceGenHotDesignArtifactKind.Style or
            SourceGenHotDesignArtifactKind.ControlTheme or
            SourceGenHotDesignArtifactKind.ResourceDictionary or
            SourceGenHotDesignArtifactKind.Template)
        {
            CanvasPreviewContent = BuildInfoPreview("Preview focuses on view artifacts. Use template editor for template/theme/style documents.");
            PreviewStatus = "Preview disabled for " + document.ArtifactKind + ".";
            return;
        }

        try
        {
            if (!XamlSourceGenRegistry.TryCreate(serviceProvider: null, uri: ActiveBuildUri, out var instance) || instance is null)
            {
                CanvasPreviewContent = BuildInfoPreview("No generated factory was registered for: " + ActiveBuildUri);
                PreviewStatus = "Preview unavailable.";
                return;
            }

            if (instance is Control control)
            {
                CanvasPreviewContent = control;
                PreviewStatus = "Preview ready for " + document.BuildUri;
                return;
            }

            CanvasPreviewContent = BuildInfoPreview(
                "Generated artifact type '" + instance.GetType().FullName + "' is not a Control and cannot be hosted in preview.");
            PreviewStatus = "Preview unavailable for non-control artifact.";
        }
        catch (Exception ex)
        {
            CanvasPreviewContent = BuildInfoPreview("Preview failed: " + ex.Message);
            PreviewStatus = "Preview error.";
        }
    }

    private static Control BuildInfoPreview(string message)
    {
        return new Border
        {
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        };
    }

    private async Task ApplyXamlAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri))
        {
            StatusMessage = "Select a document first.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.ApplyDocumentTextAsync(ActiveBuildUri, XamlText);
        StatusMessage = result.Message;
        RefreshAll();
    }

    private async Task ApplyTemplateXamlAsync()
    {
        if (SelectedTemplateDocument is null)
        {
            StatusMessage = "Select a template document first.";
            return;
        }

        var result = await XamlSourceGenHotDesignTool.ApplyDocumentTextAsync(
            SelectedTemplateDocument.BuildUri,
            TemplateXamlText);
        StatusMessage = result.Message;
        ActiveBuildUri = SelectedTemplateDocument.BuildUri;
        RefreshAll();
    }

    private async Task UndoAsync()
    {
        var result = await XamlSourceGenHotDesignTool.UndoAsync(string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri);
        StatusMessage = result.Message;
        RefreshAll();
    }

    private async Task RedoAsync()
    {
        var result = await XamlSourceGenHotDesignTool.RedoAsync(string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri);
        StatusMessage = result.Message;
        RefreshAll();
    }

    private async Task InsertElementAsync()
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
        RefreshAll();
    }

    private async Task RemoveElementAsync()
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
        RefreshAll();
    }

    private async Task ApplyPropertyAsync()
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
        RefreshAll();
    }

    private async Task RemovePropertyAsync()
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
        RefreshAll();
    }

    private async Task ApplyQuickSetAsync()
    {
        if (SelectedQuickSet is null)
        {
            return;
        }

        PropertyValue = SelectedQuickSet.Value;
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
        RefreshWorkspace();
    }

    private async Task RunCommandAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PostUi(() => StatusMessage = "Studio command failed: " + ex.Message);
        }
    }

    private void OnHotDesignUpdateApplied(SourceGenHotDesignApplyResult result)
    {
        PostUi(RefreshAll);
    }

    private void OnHotReloadCompleted(SourceGenHotReloadUpdateContext context)
    {
        PostUi(RefreshAll);
    }

    private void OnStudioStatusChanged(SourceGenStudioStatusSnapshot snapshot)
    {
        PostUi(RefreshStudioStatus);
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

    private static IReadOnlyList<SourceGenHotDesignToolboxItem> FlattenToolbox(
        IReadOnlyList<SourceGenHotDesignToolboxCategory> categories)
    {
        if (categories.Count == 0)
        {
            return Array.Empty<SourceGenHotDesignToolboxItem>();
        }

        var items = new List<SourceGenHotDesignToolboxItem>();
        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var category = categories[categoryIndex];
            for (var itemIndex = 0; itemIndex < category.Items.Count; itemIndex++)
            {
                items.Add(category.Items[itemIndex]);
            }
        }

        return items;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private static void PostUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal sealed class StudioRelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public StudioRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public void NotifyCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
