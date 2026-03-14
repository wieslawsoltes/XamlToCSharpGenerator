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
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenStudioShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SourceGenStudioOptions _options;
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
    private readonly StudioRelayCommand _togglePropertyPinCommand;
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
    private string _remoteStatusText = "Remote: disabled";
    private string _remoteVncEndpoint = string.Empty;
    private string _previewStatus = "Preview unavailable.";
    private string _xamlText = string.Empty;
    private string _templateXamlText = string.Empty;
    private string _activeBuildUri = string.Empty;
    private string _selectedElementId = "0";
    private string _propertyName = string.Empty;
    private string _propertyValue = string.Empty;
    private string _propertySearchText = string.Empty;
    private string _selectedPropertyCategory = "All";
    private string _selectedPropertySource = "All";
    private bool _showOnlySetProperties;
    private string _newElementName = "Button";
    private string _formFactor = "Desktop";
    private double _zoom = 1.0;
    private bool _canUndo;
    private bool _canRedo;
    private bool _isRefreshing;
    private bool _isDisposed;
    private bool _isCanvasPreviewDirty = true;
    private object? _canvasPreviewContent;
    private IReadOnlyList<SourceGenHotDesignElementNode> _liveElements = Array.Empty<SourceGenHotDesignElementNode>();
    private IReadOnlyList<SourceGenHotDesignPropertyEntry> _sourceProperties = Array.Empty<SourceGenHotDesignPropertyEntry>();
    private WeakReference<Control>? _liveRootControlReference;
    private WeakReference<Control>? _lastProjectedLiveRootReference;
    private string? _lastProjectedBuildUri;
    private string? _lastProjectedSelectedSourceElementId;
    private string? _lastProjectedSearchText;
    private SourceGenHotDesignHitTestMode _lastProjectedHitTestMode;
    private int _lastProjectedLiveTreeStamp;
    private int _liveProjectionBuildCount;
    private string? _selectedLiveElementId;
    private SourceGenHotDesignWorkspaceMode _workspaceMode = SourceGenHotDesignWorkspaceMode.Design;
    private SourceGenHotDesignPropertyFilterMode _propertyFilterMode = SourceGenHotDesignPropertyFilterMode.Smart;
    private SourceGenHotDesignHitTestMode _hitTestMode = SourceGenHotDesignHitTestMode.Logical;
    private readonly HashSet<string> _pinnedPropertyKeys = new(StringComparer.OrdinalIgnoreCase);

    public XamlSourceGenStudioShellViewModel(SourceGenStudioOptions options)
    {
        _options = options.Clone();
        WorkspaceModes = Enum.GetValues<SourceGenHotDesignWorkspaceMode>();
        PropertyFilterModes = Enum.GetValues<SourceGenHotDesignPropertyFilterMode>();
        HitTestModes = Enum.GetValues<SourceGenHotDesignHitTestMode>();

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
        _togglePropertyPinCommand = new StudioRelayCommand(value => TogglePropertyPin(value as SourceGenHotDesignPropertyEntry));
        _togglePanelCommand = new StudioRelayCommand(value => TogglePanel(value as string));
        _hitTestMode = XamlSourceGenHotDesignTool.GetHitTestMode();

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

    public ObservableCollection<SourceGenHotDesignElementNode> DisplayElements { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyEntry> Properties { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyEntry> FilteredProperties { get; } = [];

    public ObservableCollection<SourceGenHotDesignPropertyQuickSet> SelectedPropertyQuickSets { get; } = [];

    public ObservableCollection<string> PropertyCategories { get; } = [];

    public ObservableCollection<string> PropertySources { get; } = [];

    public ObservableCollection<SourceGenHotDesignToolboxItem> ToolboxItems { get; } = [];

    public ObservableCollection<string> StudioOperationLines { get; } = [];

    public IReadOnlyList<SourceGenHotDesignWorkspaceMode> WorkspaceModes { get; }

    public IReadOnlyList<SourceGenHotDesignPropertyFilterMode> PropertyFilterModes { get; }

    public IReadOnlyList<SourceGenHotDesignHitTestMode> HitTestModes { get; }

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

    public StudioRelayCommand TogglePropertyPinCommand => _togglePropertyPinCommand;

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
            TemplateXamlText = XamlSourceGenHotDesignTool.TryGetCurrentDocumentText(value.BuildUri, out var templateText)
                ? templateText
                : string.Empty;
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

            if (value.IsLive)
            {
                SelectLiveElement(value);
                return;
            }

            _selectedLiveElementId = null;
            SelectSourceElement(
                string.IsNullOrWhiteSpace(value.SourceBuildUri) ? ActiveBuildUri : value.SourceBuildUri,
                string.IsNullOrWhiteSpace(value.SourceElementId) ? value.Id : value.SourceElementId);
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
        set
        {
            if (!SetProperty(ref _searchText, value) || _isRefreshing)
            {
                return;
            }

            RefreshWorkspace();
        }
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

    public string RemoteStatusText
    {
        get => _remoteStatusText;
        set => SetProperty(ref _remoteStatusText, value);
    }

    public string RemoteVncEndpoint
    {
        get => _remoteVncEndpoint;
        set => SetProperty(ref _remoteVncEndpoint, value);
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

            _selectedLiveElementId = null;
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

    public string PropertySearchText
    {
        get => _propertySearchText;
        set
        {
            if (!SetProperty(ref _propertySearchText, value))
            {
                return;
            }

            RefreshFilteredProperties();
        }
    }

    public string SelectedPropertyCategory
    {
        get => _selectedPropertyCategory;
        set
        {
            if (!SetProperty(ref _selectedPropertyCategory, value))
            {
                return;
            }

            RefreshFilteredProperties();
        }
    }

    public string SelectedPropertySource
    {
        get => _selectedPropertySource;
        set
        {
            if (!SetProperty(ref _selectedPropertySource, value))
            {
                return;
            }

            RefreshFilteredProperties();
        }
    }

    public bool ShowOnlySetProperties
    {
        get => _showOnlySetProperties;
        set
        {
            if (!SetProperty(ref _showOnlySetProperties, value))
            {
                return;
            }

            RefreshFilteredProperties();
        }
    }

    public string PropertySummaryText =>
        FilteredProperties.Count + "/" + Properties.Count +
        " shown | " + Properties.Count(static property => property.IsSet) +
        " local | " + Properties.Count(static property => property.IsAttached) +
        " attached.";

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
            NotifyWorkspaceModeChanged();
        }
    }

    public bool IsInteractiveMode => WorkspaceMode == SourceGenHotDesignWorkspaceMode.Interactive;

    public bool IsDesignMode => WorkspaceMode == SourceGenHotDesignWorkspaceMode.Design;

    public bool IsAgentMode => WorkspaceMode == SourceGenHotDesignWorkspaceMode.Agent;

    public string LiveSurfaceModeText => WorkspaceMode switch
    {
        SourceGenHotDesignWorkspaceMode.Design => "Design mode: click live controls to select XAML elements.",
        SourceGenHotDesignWorkspaceMode.Agent => "Agent mode: click live controls to inspect and refine generated output.",
        _ => "Interactive mode: the app behaves normally."
    };

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

    public SourceGenHotDesignHitTestMode HitTestMode
    {
        get => _hitTestMode;
        set
        {
            if (!SetProperty(ref _hitTestMode, value))
            {
                return;
            }

            XamlSourceGenHotDesignTool.SetHitTestMode(value);
            if (value == SourceGenHotDesignHitTestMode.Logical)
            {
                _selectedLiveElementId = null;
            }

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
        InvalidateProjectedLiveTree();
        RefreshStudioStatus();
        RefreshWorkspace();
        InvalidateCanvasPreview();
    }

    public void EnsureCanvasPreviewLoaded()
    {
        if (!_isCanvasPreviewDirty && CanvasPreviewContent is not null)
        {
            return;
        }

        RefreshCanvasPreview();
    }

    public void SelectElement(string? elementId)
    {
        if (string.IsNullOrWhiteSpace(ActiveBuildUri) || string.IsNullOrWhiteSpace(elementId))
        {
            return;
        }

        _selectedLiveElementId = null;
        SelectSourceElement(ActiveBuildUri, elementId);
    }

    private void SelectLiveElement(SourceGenHotDesignElementNode element)
    {
        _selectedLiveElementId = element.Id;

        if (!string.IsNullOrWhiteSpace(element.SourceElementId))
        {
            SelectSourceElement(
                string.IsNullOrWhiteSpace(element.SourceBuildUri) ? ActiveBuildUri : element.SourceBuildUri!,
                element.SourceElementId,
                retainLiveSelection: true);
            return;
        }

        RefreshPropertiesForCurrentSelection(element);
        SynchronizeSelectedPropertySelection();
    }

    private void SelectSourceElement(string? buildUri, string elementId, bool retainLiveSelection = false)
    {
        if (string.IsNullOrWhiteSpace(buildUri) || string.IsNullOrWhiteSpace(elementId))
        {
            return;
        }

        var documentChanged = !string.Equals(_activeBuildUri, buildUri, StringComparison.OrdinalIgnoreCase);
        if (!retainLiveSelection)
        {
            _selectedLiveElementId = null;
        }

        if (documentChanged)
        {
            _activeBuildUri = buildUri;
            XamlSourceGenHotDesignTool.SelectDocument(buildUri);
            OnPropertyChanged(nameof(ActiveBuildUri));
        }

        SelectedElementId = elementId;
        XamlSourceGenHotDesignTool.SelectElement(buildUri, elementId);
        if (documentChanged)
        {
            RefreshAll();
        }
        else
        {
            RefreshWorkspace();
        }
    }

    public bool TryHandleLiveSurfacePointerPressed(object? pointerSource)
    {
        if (IsInteractiveMode)
        {
            return false;
        }

        if (TryResolveControl(pointerSource) is not { } control)
        {
            return false;
        }

        if (!TrySelectElementForControl(control, HitTestMode))
        {
            return false;
        }

        return true;
    }

    private void RefreshWorkspace()
    {
        var snapshot = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(
            string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri,
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);

        _isRefreshing = true;
        try
        {
            ReplaceCollection(Documents, snapshot.Documents, DocumentDescriptorEquals);
            ReplaceCollection(Elements, snapshot.Elements, ElementNodeEquals);
            RefreshDisplayElements(snapshot.SelectedElementId);
            ReplaceCollection(ToolboxItems, FlattenToolbox(snapshot.Toolbox), ToolboxItemEquals);

            var templates = snapshot.Documents.Where(static document =>
                document.ArtifactKind == SourceGenHotDesignArtifactKind.Template ||
                document.DocumentRole == SourceGenHotDesignDocumentRole.Template).ToArray();
            ReplaceCollection(TemplateDocuments, templates, DocumentDescriptorEquals);

            ActiveBuildUri = snapshot.ActiveBuildUri ?? string.Empty;
            SelectedElementId = snapshot.SelectedElementId ?? "0";
            XamlText = snapshot.CurrentXamlText ?? string.Empty;
            WorkspaceMode = snapshot.Mode;
            PropertyFilterMode = snapshot.PropertyFilterMode;
            _hitTestMode = XamlSourceGenHotDesignTool.GetHitTestMode();
            OnPropertyChanged(nameof(HitTestMode));
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
            if (selectedTemplateDocument is not null &&
                XamlSourceGenHotDesignTool.TryGetCurrentDocumentText(selectedTemplateDocument.BuildUri, out var selectedTemplateText))
            {
                TemplateXamlText = selectedTemplateText;
            }
            else
            {
                TemplateXamlText = string.Empty;
            }

            var selectedElement = ResolveSelectedElement(snapshot.SelectedElementId);
            _selectedElement = selectedElement;
            OnPropertyChanged(nameof(SelectedElement));

            _sourceProperties = snapshot.Properties;
            RefreshPropertiesForCurrentSelection(selectedElement);
            SynchronizeSelectedPropertySelection();

            StatusMessage = snapshot.Status.IsEnabled
                ? "Hot Design active. Documents=" + snapshot.Documents.Count + ", Elements=" + DisplayElements.Count + "."
                : "Hot Design is currently disabled.";
            NotifyWorkspaceModeChanged();
            OnPropertyChanged(nameof(PropertySummaryText));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public void UpdateLiveElementTree(Control? liveRootControl)
    {
        if (liveRootControl is null)
        {
            return;
        }

        var preferredBuildUri = string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri;
        var selectedSourceElementId = string.IsNullOrWhiteSpace(SelectedElementId) ? null : SelectedElementId;
        var searchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;

        _liveRootControlReference = new WeakReference<Control>(liveRootControl);
        if (CanReuseProjectedLiveTree(
                liveRootControl,
                preferredBuildUri,
                selectedSourceElementId,
                searchText,
                HitTestMode))
        {
            return;
        }

        _liveProjectionBuildCount++;
        var projectedLiveElements = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
            liveRootControl,
            HitTestMode,
            preferredBuildUri,
            selectedSourceElementId,
            searchText);

        CaptureProjectedLiveTreeInputs(
            liveRootControl,
            preferredBuildUri,
            selectedSourceElementId,
            searchText,
            HitTestMode);

        if (LiveElementTreeEquals(_liveElements, projectedLiveElements))
        {
            return;
        }

        _liveElements = projectedLiveElements;
        if (ShouldUseLiveElementTree())
        {
            RefreshDisplayElements(SelectedElementId);
        }
    }

    public void ClearLiveElementTree()
    {
        _liveRootControlReference = null;
        InvalidateProjectedLiveTree();
        var wasUsingLiveElementTree = ShouldUseLiveElementTree();
        if (_liveElements.Count == 0)
        {
            return;
        }

        _liveElements = Array.Empty<SourceGenHotDesignElementNode>();
        if (wasUsingLiveElementTree)
        {
            RefreshDisplayElements(SelectedElementId);
            RefreshPropertiesForCurrentSelection(_selectedElement);
            SynchronizeSelectedPropertySelection();
        }
    }

    private void RefreshDisplayElements(string? selectedElementId)
    {
        var source = ShouldUseLiveElementTree()
            ? _liveElements
            : Elements.ToArray();
        ReplaceCollection(DisplayElements, source, ElementNodeEquals);

        _selectedElement = ResolveSelectedElement(selectedElementId);
        OnPropertyChanged(nameof(SelectedElement));
    }

    private SourceGenHotDesignElementNode? ResolveSelectedElement(string? selectedElementId)
    {
        if (DisplayElements.Count == 0)
        {
            return null;
        }

        SourceGenHotDesignElementNode? selectedLive = null;
        if (ShouldUseLiveElementTree() && !string.IsNullOrWhiteSpace(_selectedLiveElementId))
        {
            selectedLive = FindById(DisplayElements, _selectedLiveElementId);
            if (selectedLive is not null && string.IsNullOrWhiteSpace(selectedLive.SourceElementId))
            {
                return selectedLive;
            }
        }

        var selected = FindBySourceElementId(DisplayElements, selectedElementId);
        if (selected is not null)
        {
            return selected;
        }

        if (selectedLive is not null)
        {
            return selectedLive;
        }

        return FindById(DisplayElements, selectedElementId);
    }

    private void RefreshPropertiesForCurrentSelection(SourceGenHotDesignElementNode? selectedElement)
    {
        IReadOnlyList<SourceGenHotDesignPropertyEntry> properties = _sourceProperties;
        var propertyOwnerTypeName = selectedElement?.TypeName;

        if (selectedElement?.IsLive == true &&
            (string.IsNullOrWhiteSpace(selectedElement.SourceElementId) || _sourceProperties.Count == 0) &&
            TryResolveLiveControl(selectedElement, out var liveControl) &&
            liveControl is not null)
        {
            properties = XamlSourceGenHotDesignCoreTools.BuildRuntimePropertyEntries(liveControl, PropertyFilterMode);
            propertyOwnerTypeName = liveControl.GetType().Name;
        }

        var pinnedProperties = ApplyPinnedStates(properties, ActiveBuildUri, propertyOwnerTypeName);
        ReplaceCollection(Properties, pinnedProperties, PropertyEntryEquals);
        RebuildPropertyFilters();
        RefreshFilteredProperties();
    }

    private void SynchronizeSelectedPropertySelection()
    {
        var selectedProperty = Properties.FirstOrDefault(property =>
            string.Equals(property.Name, PropertyName, StringComparison.OrdinalIgnoreCase));
        SelectedProperty = selectedProperty;
    }

    private bool TryResolveLiveControl(SourceGenHotDesignElementNode element, out Control? liveControl)
    {
        liveControl = null;
        if (_liveRootControlReference is null ||
            !_liveRootControlReference.TryGetTarget(out var liveRootControl))
        {
            return false;
        }

        liveControl = XamlSourceGenStudioLiveTreeProjectionService.ResolveLiveControlForElement(
            liveRootControl,
            HitTestMode,
            element,
            string.IsNullOrWhiteSpace(ActiveBuildUri) ? null : ActiveBuildUri);
        return liveControl is not null;
    }

    private bool CanReuseProjectedLiveTree(
        Control liveRootControl,
        string? buildUri,
        string? selectedSourceElementId,
        string? searchText,
        SourceGenHotDesignHitTestMode hitTestMode)
    {
        if (_lastProjectedLiveRootReference is null ||
            !_lastProjectedLiveRootReference.TryGetTarget(out var lastProjectedRoot) ||
            !ReferenceEquals(lastProjectedRoot, liveRootControl))
        {
            return false;
        }

        var liveTreeStamp = ComputeLiveTreeStamp(liveRootControl, hitTestMode);
        return hitTestMode == _lastProjectedHitTestMode &&
               liveTreeStamp == _lastProjectedLiveTreeStamp &&
               string.Equals(buildUri, _lastProjectedBuildUri, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(selectedSourceElementId, _lastProjectedSelectedSourceElementId, StringComparison.Ordinal) &&
               string.Equals(searchText, _lastProjectedSearchText, StringComparison.Ordinal);
    }

    private void CaptureProjectedLiveTreeInputs(
        Control liveRootControl,
        string? buildUri,
        string? selectedSourceElementId,
        string? searchText,
        SourceGenHotDesignHitTestMode hitTestMode)
    {
        _lastProjectedLiveRootReference = new WeakReference<Control>(liveRootControl);
        _lastProjectedBuildUri = buildUri;
        _lastProjectedSelectedSourceElementId = selectedSourceElementId;
        _lastProjectedSearchText = searchText;
        _lastProjectedHitTestMode = hitTestMode;
        _lastProjectedLiveTreeStamp = ComputeLiveTreeStamp(liveRootControl, hitTestMode);
    }

    private void InvalidateProjectedLiveTree()
    {
        _lastProjectedLiveRootReference = null;
        _lastProjectedBuildUri = null;
        _lastProjectedSelectedSourceElementId = null;
        _lastProjectedSearchText = null;
        _lastProjectedLiveTreeStamp = 0;
    }

    private static int ComputeLiveTreeStamp(Control liveRootControl, SourceGenHotDesignHitTestMode hitTestMode)
    {
        var hash = new HashCode();
        hash.Add((int)hitTestMode);

        var stack = new Stack<(Control Control, int SiblingIndex)>();
        stack.Push((liveRootControl, 0));

        while (stack.Count > 0)
        {
            var (control, siblingIndex) = stack.Pop();
            hash.Add(siblingIndex);
            hash.Add(control.GetType().FullName, StringComparer.Ordinal);
            hash.Add(control.Name, StringComparer.Ordinal);

            var children = EnumerateLiveTreeChildren(control, hitTestMode).ToArray();
            hash.Add(children.Length);
            for (var index = children.Length - 1; index >= 0; index--)
            {
                stack.Push((children[index], index));
            }
        }

        return hash.ToHashCode();
    }

    private static IEnumerable<Control> EnumerateLiveTreeChildren(Control control, SourceGenHotDesignHitTestMode hitTestMode)
    {
        if (hitTestMode == SourceGenHotDesignHitTestMode.Logical)
        {
            foreach (var child in ((ILogical)control).GetLogicalChildren())
            {
                if (child is Control childControl)
                {
                    yield return childControl;
                }
            }

            yield break;
        }

        foreach (var visualChild in control.GetVisualChildren())
        {
            if (visualChild is Control childControl)
            {
                yield return childControl;
            }
        }
    }

    private bool ShouldUseLiveElementTree()
    {
        return _liveElements.Count > 0 &&
               (HitTestMode == SourceGenHotDesignHitTestMode.Visual || Elements.Count == 0);
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

        if (!snapshot.Remote.IsEnabled)
        {
            RemoteStatusText = "Remote: disabled";
        }
        else if (snapshot.Remote.IsListening)
        {
            RemoteStatusText = "Remote: listening " + snapshot.Remote.Host + ":" + snapshot.Remote.Port +
                               " (clients=" + snapshot.Remote.ActiveClientCount + ")";
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.Remote.LastError))
        {
            RemoteStatusText = "Remote: error - " + snapshot.Remote.LastError;
        }
        else
        {
            RemoteStatusText = "Remote: configured " + snapshot.Remote.Host + ":" + snapshot.Remote.Port;
        }

        RemoteVncEndpoint = snapshot.Remote.VncEndpoint ?? string.Empty;
    }

    private void RefreshCanvasPreview()
    {
        _isCanvasPreviewDirty = false;

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

    private void InvalidateCanvasPreview()
    {
        _isCanvasPreviewDirty = true;

        if (!_options.EnableExternalWindow)
        {
            return;
        }

        CanvasPreviewContent = BuildInfoPreview("Preview is loaded on demand. Use Refresh Preview.");
        PreviewStatus = "Preview pending.";
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

        if (IsRuntimeOnlySelection())
        {
            StatusMessage = "The current visual selection is runtime-only and cannot be edited in XAML.";
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

        if (IsRuntimeOnlySelection())
        {
            StatusMessage = "The current visual selection is runtime-only and cannot be removed from XAML.";
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

        if (IsRuntimeOnlySelection())
        {
            StatusMessage = "The current visual selection is runtime-only; property edits must target an authored XAML element.";
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

        if (IsRuntimeOnlySelection())
        {
            StatusMessage = "The current visual selection is runtime-only; properties cannot be removed from XAML.";
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

    private bool IsRuntimeOnlySelection()
    {
        return SelectedElement?.IsLive == true &&
               string.IsNullOrWhiteSpace(SelectedElement.SourceElementId);
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

    private static SourceGenHotDesignElementNode? FindBySourceElementId(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        string? sourceElementId)
    {
        if (string.IsNullOrWhiteSpace(sourceElementId))
        {
            return null;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (string.Equals(node.SourceElementId, sourceElementId, StringComparison.Ordinal))
            {
                return node;
            }

            var found = FindBySourceElementId(node.Children, sourceElementId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private bool TrySelectElementForControl(Control control, SourceGenHotDesignHitTestMode mode)
    {
        var controlNames = new List<string>(4);
        var controlTypeNames = new List<string>(6);
        XamlSourceGenStudioHitTestingService.CollectIdentityCandidates(
            control,
            mode,
            controlNames,
            controlTypeNames);

        if (controlNames.Count == 0 && controlTypeNames.Count == 0)
        {
            return false;
        }

        if (DisplayElements.Count > 0)
        {
            var flattened = FlattenElements(DisplayElements);
            if (flattened.Count > 0 &&
                XamlSourceGenStudioSelectionResolver.TryFindBestMatchingElementNode(
                    flattened,
                    controlNames,
                    controlTypeNames,
                    out var matchInCurrentDocument))
            {
                if (matchInCurrentDocument!.IsLive)
                {
                    if (!string.IsNullOrWhiteSpace(matchInCurrentDocument.SourceElementId))
                    {
                        SelectLiveElement(matchInCurrentDocument);
                        return true;
                    }

                    _selectedElement = matchInCurrentDocument;
                    OnPropertyChanged(nameof(SelectedElement));
                    SelectLiveElement(matchInCurrentDocument);
                    return true;
                }

                if (!matchInCurrentDocument.IsLive)
                {
                    SelectSourceElement(ActiveBuildUri, matchInCurrentDocument.Id);
                    return true;
                }
            }
        }

        if (!XamlSourceGenStudioSelectionResolver.TryResolveSourceElementForControl(
                control,
                mode,
                ActiveBuildUri,
                out var resolvedBuildUri,
                out var resolvedElementId) ||
            string.IsNullOrWhiteSpace(resolvedBuildUri) ||
            string.IsNullOrWhiteSpace(resolvedElementId))
        {
            return false;
        }

        SelectSourceElement(resolvedBuildUri, resolvedElementId);
        return true;
    }

    private static Control? TryResolveControl(object? pointerSource)
    {
        if (pointerSource is Control control)
        {
            return control;
        }

        if (pointerSource is not Visual visual)
        {
            return null;
        }

        if (visual is Control directControl)
        {
            return directControl;
        }

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is Control ancestorControl)
            {
                return ancestorControl;
            }
        }

        return null;
    }

    private static List<SourceGenHotDesignElementNode> FlattenElements(
        IReadOnlyCollection<SourceGenHotDesignElementNode> roots)
    {
        var output = new List<SourceGenHotDesignElementNode>();
        foreach (var root in roots)
        {
            Flatten(root, output);
        }

        return output;
    }

    private static void Flatten(SourceGenHotDesignElementNode node, List<SourceGenHotDesignElementNode> output)
    {
        output.Add(node);
        for (var index = 0; index < node.Children.Count; index++)
        {
            Flatten(node.Children[index], output);
        }
    }

    private void TogglePropertyPin(SourceGenHotDesignPropertyEntry? property)
    {
        if (property is null)
        {
            return;
        }

        var key = BuildPinnedPropertyKey(ActiveBuildUri, SelectedElement?.TypeName, property.Name);
        if (!_pinnedPropertyKeys.Remove(key))
        {
            _pinnedPropertyKeys.Add(key);
        }

        var updatedProperties = ApplyPinnedStates(Properties, ActiveBuildUri, SelectedElement?.TypeName);
        ReplaceCollection(Properties, updatedProperties, PropertyEntryEquals);
        RefreshFilteredProperties();

        var selectedProperty = Properties.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));
        _selectedProperty = selectedProperty;
        OnPropertyChanged(nameof(SelectedProperty));
        OnPropertyChanged(nameof(PropertySummaryText));
    }

    private void RebuildPropertyFilters()
    {
        var categories = Properties
            .Select(static property => property.Category)
            .Where(static category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sources = Properties
            .Select(static property => property.Source)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(PropertyCategories, BuildFilterList(categories));
        ReplaceCollection(PropertySources, BuildFilterList(sources));

        if (!ContainsIgnoreCase(PropertyCategories, SelectedPropertyCategory))
        {
            _selectedPropertyCategory = "All";
            OnPropertyChanged(nameof(SelectedPropertyCategory));
        }

        if (!ContainsIgnoreCase(PropertySources, SelectedPropertySource))
        {
            _selectedPropertySource = "All";
            OnPropertyChanged(nameof(SelectedPropertySource));
        }
    }

    private void RefreshFilteredProperties()
    {
        var search = string.IsNullOrWhiteSpace(PropertySearchText) ? null : PropertySearchText.Trim();
        var includeCategoryFilter = !string.Equals(SelectedPropertyCategory, "All", StringComparison.OrdinalIgnoreCase);
        var includeSourceFilter = !string.Equals(SelectedPropertySource, "All", StringComparison.OrdinalIgnoreCase);

        var filtered = Properties
            .Where(property =>
            {
                if (ShowOnlySetProperties && !property.IsSet)
                {
                    return false;
                }

                if (includeCategoryFilter &&
                    !string.Equals(property.Category, SelectedPropertyCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (includeSourceFilter &&
                    !string.Equals(property.Source, SelectedPropertySource, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(search))
                {
                    return true;
                }

                return property.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       property.TypeName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       property.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       property.Source.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       (property.Value?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
            })
            .OrderByDescending(static property => property.IsPinned)
            .ThenByDescending(static property => property.IsSet)
            .ThenBy(static property => property.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(FilteredProperties, filtered, PropertyEntryEquals);
        if (SelectedProperty is not null &&
            FilteredProperties.All(property => !string.Equals(property.Name, SelectedProperty.Name, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedProperty = null;
        }

        OnPropertyChanged(nameof(PropertySummaryText));
    }

    private static IReadOnlyList<string> BuildFilterList(IReadOnlyList<string> values)
    {
        var output = new List<string>(values.Count + 1)
        {
            "All"
        };

        for (var index = 0; index < values.Count; index++)
        {
            output.Add(values[index]);
        }

        return output;
    }

    private IReadOnlyList<SourceGenHotDesignPropertyEntry> ApplyPinnedStates(
        IReadOnlyList<SourceGenHotDesignPropertyEntry> properties,
        string? buildUri,
        string? typeName)
    {
        if (properties.Count == 0)
        {
            return Array.Empty<SourceGenHotDesignPropertyEntry>();
        }

        var withPinnedState = new SourceGenHotDesignPropertyEntry[properties.Count];
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var key = BuildPinnedPropertyKey(buildUri, typeName, property.Name);
            withPinnedState[index] = property with
            {
                IsPinned = _pinnedPropertyKeys.Contains(key)
            };
        }

        return withPinnedState;
    }

    private static string BuildPinnedPropertyKey(string? buildUri, string? typeName, string propertyName)
    {
        return (buildUri ?? string.Empty) + "|" + (typeName ?? string.Empty) + "|" + propertyName;
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string candidate)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void NotifyWorkspaceModeChanged()
    {
        OnPropertyChanged(nameof(IsInteractiveMode));
        OnPropertyChanged(nameof(IsDesignMode));
        OnPropertyChanged(nameof(IsAgentMode));
        OnPropertyChanged(nameof(LiveSurfaceModeText));
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

    private static void ReplaceCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> source,
        Func<T, T, bool>? itemEquals = null)
    {
        itemEquals ??= static (left, right) => EqualityComparer<T>.Default.Equals(left, right);
        if (CollectionEquals(target, source, itemEquals))
        {
            return;
        }

        target.Clear();
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private static bool CollectionEquals<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> itemEquals)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!itemEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LiveElementTreeEquals(
        IReadOnlyList<SourceGenHotDesignElementNode> left,
        IReadOnlyList<SourceGenHotDesignElementNode> right)
    {
        return CollectionEquals(left, right, ElementNodeEquals);
    }

    private static bool ElementNodeEquals(
        SourceGenHotDesignElementNode left,
        SourceGenHotDesignElementNode right)
    {
        if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
            !string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) ||
            !string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal) ||
            !string.Equals(left.XamlName, right.XamlName, StringComparison.Ordinal) ||
            !string.Equals(left.Classes, right.Classes, StringComparison.Ordinal) ||
            left.Depth != right.Depth ||
            left.IsSelected != right.IsSelected ||
            left.Line != right.Line ||
            left.IsExpanded != right.IsExpanded ||
            left.DescendantCount != right.DescendantCount ||
            !string.Equals(left.SourceBuildUri, right.SourceBuildUri, StringComparison.Ordinal) ||
            !string.Equals(left.SourceElementId, right.SourceElementId, StringComparison.Ordinal) ||
            left.IsLive != right.IsLive ||
            left.Children.Count != right.Children.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Children.Count; index++)
        {
            if (!ElementNodeEquals(left.Children[index], right.Children[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DocumentDescriptorEquals(
        SourceGenHotDesignDocumentDescriptor left,
        SourceGenHotDesignDocumentDescriptor right)
    {
        return left.RootType == right.RootType &&
               string.Equals(left.BuildUri, right.BuildUri, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase) &&
               left.LiveInstanceCount == right.LiveInstanceCount &&
               left.DocumentRole == right.DocumentRole &&
               left.ArtifactKind == right.ArtifactKind &&
               StringListEquals(left.ScopeHints, right.ScopeHints, StringComparer.OrdinalIgnoreCase);
    }

    private static bool PropertyEntryEquals(
        SourceGenHotDesignPropertyEntry left,
        SourceGenHotDesignPropertyEntry right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.Value, right.Value, StringComparison.Ordinal) &&
               string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal) &&
               left.IsSet == right.IsSet &&
               left.IsAttached == right.IsAttached &&
               left.IsMarkupExtension == right.IsMarkupExtension &&
               string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
               string.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
               string.Equals(left.OwnerTypeName, right.OwnerTypeName, StringComparison.Ordinal) &&
               string.Equals(left.EditorKind, right.EditorKind, StringComparison.Ordinal) &&
               left.IsPinned == right.IsPinned &&
               left.IsReadOnly == right.IsReadOnly &&
               left.CanReset == right.CanReset &&
               CollectionEquals(left.QuickSets, right.QuickSets, EqualityComparer<SourceGenHotDesignPropertyQuickSet>.Default.Equals) &&
               StringListEquals(left.EnumOptions, right.EnumOptions, StringComparer.Ordinal);
    }

    private static bool ToolboxItemEquals(
        SourceGenHotDesignToolboxItem left,
        SourceGenHotDesignToolboxItem right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
               string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
               string.Equals(left.XamlSnippet, right.XamlSnippet, StringComparison.Ordinal) &&
               left.IsProjectControl == right.IsProjectControl &&
               StringListEquals(left.Tags, right.Tags, StringComparer.Ordinal);
    }

    private static bool StringListEquals(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right,
        StringComparer comparer)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
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
