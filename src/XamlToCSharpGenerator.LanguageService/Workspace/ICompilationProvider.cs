using System;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

public interface ICompilationProvider : IDisposable
{
    Task<CompilationSnapshot> GetCompilationAsync(string filePath, string? workspaceRoot, CancellationToken cancellationToken);

    void Invalidate(string filePath);
}
