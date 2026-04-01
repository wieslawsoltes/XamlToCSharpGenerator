using System.IO;

namespace EditorAvaloniaSample.Models;

public sealed record SampleWorkspaceDocument(string Title, string RelativePath, string Description)
{
    public string FileName => Path.GetFileName(RelativePath);
}
