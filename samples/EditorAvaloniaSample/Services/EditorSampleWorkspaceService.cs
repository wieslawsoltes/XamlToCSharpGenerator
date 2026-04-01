using System;
using System.Collections.Immutable;
using System.IO;
using EditorAvaloniaSample.Models;

namespace EditorAvaloniaSample.Services;

public sealed class EditorSampleWorkspaceService
{
    private static readonly ImmutableArray<SampleWorkspaceDocument> Documents = ImmutableArray.Create(
        new SampleWorkspaceDocument(
            "App.axaml",
            "App.axaml",
            "Sample application theme bootstrap with compact Fluent configuration."),
        new SampleWorkspaceDocument(
            "MainWindow.axaml",
            "MainWindow.axaml",
            "The sample shell that hosts the explorer, editor surface, and bottom problem/output panel."),
        new SampleWorkspaceDocument(
            "PreviewCardView.axaml",
            Path.Combine("Workspace", "PreviewCardView.axaml"),
            "A view-focused file with compiled bindings, typed templates, and a small form layout."),
        new SampleWorkspaceDocument(
            "ToolboxSurfaceView.axaml",
            Path.Combine("Workspace", "ToolboxSurfaceView.axaml"),
            "A denser layout with resources, list templates, and selection controls."));

    public SampleWorkspace LoadWorkspace()
    {
        var rootPath = FindProjectRoot();
        if (rootPath is null)
        {
            throw new DirectoryNotFoundException(
                "The sample workspace could not be located. Run the app from the repository build output so it can resolve the project files.");
        }

        return new SampleWorkspace(rootPath, Documents);
    }

    public string ReadDocumentText(SampleWorkspace workspace, SampleWorkspaceDocument document)
    {
        return File.ReadAllText(GetAbsolutePath(workspace, document));
    }

    public string GetDocumentUri(SampleWorkspace workspace, SampleWorkspaceDocument document)
    {
        return new Uri(GetAbsolutePath(workspace, document)).AbsoluteUri;
    }

    public string CreateBrokenVariant(string text, SampleWorkspaceDocument document)
    {
        if (string.Equals(document.RelativePath, Path.Combine("Workspace", "PreviewCardView.axaml"), StringComparison.Ordinal))
        {
            return text.Replace(
                "x:DataType=\"vm:EditorPreviewDataViewModel\"",
                "x:DataType=\"vm:MissingPreviewDataViewModel\"",
                StringComparison.Ordinal);
        }

        return text.Replace(
            "<TextBlock Text=\"Suggested Tools\"",
            "<UnknownControl />\n      <TextBlock Text=\"Suggested Tools\"",
            StringComparison.Ordinal);
    }

    private static string GetAbsolutePath(SampleWorkspace workspace, SampleWorkspaceDocument document)
    {
        return Path.Combine(workspace.RootPath, document.RelativePath);
    }

    private static string? FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectFile = Path.Combine(directory.FullName, "EditorAvaloniaSample.csproj");
            if (File.Exists(projectFile))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
