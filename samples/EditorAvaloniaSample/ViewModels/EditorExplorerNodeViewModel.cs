using System.Collections.ObjectModel;
using EditorAvaloniaSample.Models;
using ReactiveUI;

namespace EditorAvaloniaSample.ViewModels;

public sealed class EditorExplorerNodeViewModel : ReactiveObject
{
    private bool _isExpanded;

    public EditorExplorerNodeViewModel(
        string displayName,
        string relativePath,
        bool isFolder,
        SampleWorkspaceDocument? document = null)
    {
        DisplayName = displayName;
        RelativePath = relativePath;
        IsFolder = isFolder;
        Document = document;
    }

    public string DisplayName { get; }

    public string RelativePath { get; }

    public bool IsFolder { get; }

    public SampleWorkspaceDocument? Document { get; }

    public ObservableCollection<EditorExplorerNodeViewModel> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }
}
