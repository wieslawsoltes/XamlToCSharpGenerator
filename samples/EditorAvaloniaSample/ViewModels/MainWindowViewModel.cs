using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using EditorAvaloniaSample.Models;
using EditorAvaloniaSample.Services;
using ReactiveUI;
using System.Reactive;

namespace EditorAvaloniaSample.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    private readonly EditorSampleWorkspaceService _workspaceService;
    private readonly SampleWorkspace _workspace;

    private EditorExplorerNodeViewModel? _selectedExplorerNode;
    private SampleWorkspaceDocument? _selectedDocument;
    private string _currentDocumentTitle = "No document loaded";
    private string _currentDocumentDescription = "Select a sample workspace file to open it in the embedded AXAML editor.";
    private string _currentDocumentPath = string.Empty;
    private string _editorText = string.Empty;
    private string _documentUri = string.Empty;
    private string _workspaceRoot = string.Empty;
    private string _workspaceDisplayName = "Workspace";
    private string _statusMessage;

    public MainWindowViewModel(EditorSampleWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        ExplorerNodes = [];
        OutputLines = [];
        _statusMessage = "Locating the sample workspace.";

        RestoreDocumentCommand = ReactiveCommand.Create(RestoreSelectedDocument);
        IntroduceErrorCommand = ReactiveCommand.Create(IntroduceErrorIntoSelectedDocument);

        try
        {
            _workspace = _workspaceService.LoadWorkspace();
            WorkspaceRoot = _workspace.RootPath;
            WorkspaceDisplayName = Path.GetFileName(_workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            BuildExplorer(_workspace);
            StatusMessage = "Select a workspace file, edit it in place, and use Ctrl+Space for completions.";
            AppendOutput("workspace", $"Opened sample workspace at '{_workspace.RootPath}'.");
            SelectFirstDocument();
        }
        catch (Exception ex)
        {
            _workspace = SampleWorkspace.Empty;
            StatusMessage = ex.Message;
            AppendOutput("error", ex.Message);
        }
    }

    public ObservableCollection<EditorExplorerNodeViewModel> ExplorerNodes { get; }

    public ObservableCollection<string> OutputLines { get; }

    public ReactiveCommand<Unit, Unit> RestoreDocumentCommand { get; }

    public ReactiveCommand<Unit, Unit> IntroduceErrorCommand { get; }

    public EditorExplorerNodeViewModel? SelectedExplorerNode
    {
        get => _selectedExplorerNode;
        set
        {
            if (EqualityComparer<EditorExplorerNodeViewModel?>.Default.Equals(_selectedExplorerNode, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedExplorerNode, value);
            if (value?.Document is not null)
            {
                SelectedDocument = value.Document;
            }
        }
    }

    public SampleWorkspaceDocument? SelectedDocument
    {
        get => _selectedDocument;
        private set
        {
            if (EqualityComparer<SampleWorkspaceDocument?>.Default.Equals(_selectedDocument, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDocument, value);
            if (value is not null && _workspace != SampleWorkspace.Empty)
            {
                LoadSelectedDocument(value);
            }
        }
    }

    public string CurrentDocumentTitle
    {
        get => _currentDocumentTitle;
        private set => this.RaiseAndSetIfChanged(ref _currentDocumentTitle, value);
    }

    public string CurrentDocumentDescription
    {
        get => _currentDocumentDescription;
        private set => this.RaiseAndSetIfChanged(ref _currentDocumentDescription, value);
    }

    public string CurrentDocumentPath
    {
        get => _currentDocumentPath;
        private set => this.RaiseAndSetIfChanged(ref _currentDocumentPath, value);
    }

    public string EditorText
    {
        get => _editorText;
        set => this.RaiseAndSetIfChanged(ref _editorText, value);
    }

    public string DocumentUri
    {
        get => _documentUri;
        private set => this.RaiseAndSetIfChanged(ref _documentUri, value);
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        private set => this.RaiseAndSetIfChanged(ref _workspaceRoot, value);
    }

    public string WorkspaceDisplayName
    {
        get => _workspaceDisplayName;
        private set => this.RaiseAndSetIfChanged(ref _workspaceDisplayName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private void RestoreSelectedDocument()
    {
        if (SelectedDocument is null || _workspace == SampleWorkspace.Empty)
        {
            return;
        }

        LoadSelectedDocument(SelectedDocument);
        StatusMessage = $"Reloaded '{SelectedDocument.Title}' from disk.";
        AppendOutput("restore", $"Reloaded '{SelectedDocument.RelativePath}' from disk.");
    }

    private void IntroduceErrorIntoSelectedDocument()
    {
        if (SelectedDocument is null || _workspace == SampleWorkspace.Empty)
        {
            return;
        }

        var cleanText = _workspaceService.ReadDocumentText(_workspace, SelectedDocument);
        EditorText = _workspaceService.CreateBrokenVariant(cleanText, SelectedDocument);
        StatusMessage = $"Introduced a deliberate error into '{SelectedDocument.Title}' so the diagnostics panel has something to report.";
        AppendOutput("edit", $"Injected a deliberate error into '{SelectedDocument.RelativePath}'.");
    }

    private void LoadSelectedDocument(SampleWorkspaceDocument document)
    {
        EditorText = _workspaceService.ReadDocumentText(_workspace, document);
        DocumentUri = _workspaceService.GetDocumentUri(_workspace, document);
        CurrentDocumentTitle = document.FileName;
        CurrentDocumentDescription = document.Description;
        CurrentDocumentPath = document.RelativePath;
        StatusMessage = $"Loaded '{document.Title}'. Edit it directly, switch files, or introduce an error to see live analysis updates.";
        AppendOutput("open", $"Loaded '{document.RelativePath}'.");
    }

    private void BuildExplorer(SampleWorkspace workspace)
    {
        foreach (var document in workspace.Documents)
        {
            AddExplorerDocument(document);
        }
    }

    private void AddExplorerDocument(SampleWorkspaceDocument document)
    {
        var segments = document.RelativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var currentNodes = ExplorerNodes;
        EditorExplorerNodeViewModel? currentFolder = null;
        var currentPath = string.Empty;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            currentPath = string.IsNullOrEmpty(currentPath) ? segment : Path.Combine(currentPath, segment);
            var isLeaf = i == segments.Length - 1;
            EditorExplorerNodeViewModel? existingNode = null;
            foreach (var node in currentNodes)
            {
                if (string.Equals(node.DisplayName, segment, StringComparison.Ordinal) &&
                    string.Equals(node.RelativePath, currentPath, StringComparison.Ordinal))
                {
                    existingNode = node;
                    break;
                }
            }

            if (existingNode is null)
            {
                existingNode = new EditorExplorerNodeViewModel(
                    displayName: segment,
                    relativePath: currentPath,
                    isFolder: !isLeaf,
                    document: isLeaf ? document : null)
                {
                    IsExpanded = true
                };
                currentNodes.Add(existingNode);
            }

            currentFolder = existingNode;
            currentNodes = existingNode.Children;
        }
    }

    private void SelectFirstDocument()
    {
        foreach (var node in ExplorerNodes)
        {
            var firstDocument = FindFirstDocumentNode(node);
            if (firstDocument is not null)
            {
                SelectedExplorerNode = firstDocument;
                return;
            }
        }
    }

    private static EditorExplorerNodeViewModel? FindFirstDocumentNode(EditorExplorerNodeViewModel node)
    {
        if (node.Document is not null)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var documentNode = FindFirstDocumentNode(child);
            if (documentNode is not null)
            {
                return documentNode;
            }
        }

        return null;
    }

    private void AppendOutput(string category, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        OutputLines.Insert(0, $"[{timestamp}] {category}: {message}");
        while (OutputLines.Count > 200)
        {
            OutputLines.RemoveAt(OutputLines.Count - 1);
        }
    }
}
