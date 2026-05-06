using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

/// <summary>
/// Provides a project-less compilation snapshot for browser and sandboxed editor hosts.
/// </summary>
public sealed class BrowserCompilationProvider : ICompilationProvider
{
    /// <inheritdoc />
    public Task<CompilationSnapshot> GetCompilationAsync(
        string filePath,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new CompilationSnapshot(
            ProjectPath: workspaceRoot,
            Project: null,
            Compilation: null,
            Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
    }

    /// <inheritdoc />
    public void Invalidate(string filePath)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
