using System.Collections.Immutable;

namespace EditorAvaloniaSample.Models;

public sealed record SampleWorkspace(string RootPath, ImmutableArray<SampleWorkspaceDocument> Documents)
{
    public static SampleWorkspace Empty { get; } = new(string.Empty, ImmutableArray<SampleWorkspaceDocument>.Empty);
}
