using System;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

internal sealed class DeferredCompilationProvider : ICompilationProvider
{
    private readonly Func<ICompilationProvider> _factory;
    private readonly object _gate = new();
    private ICompilationProvider? _innerProvider;

    public DeferredCompilationProvider(Func<ICompilationProvider> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Task<CompilationSnapshot> GetCompilationAsync(
        string filePath,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        return GetInnerProvider().GetCompilationAsync(filePath, workspaceRoot, cancellationToken);
    }

    public void Invalidate(string filePath)
    {
        var provider = _innerProvider;
        provider?.Invalidate(filePath);
    }

    public void Dispose()
    {
        var provider = Interlocked.Exchange(ref _innerProvider, null);
        provider?.Dispose();
    }

    internal bool IsInitialized => _innerProvider is not null;

    private ICompilationProvider GetInnerProvider()
    {
        if (_innerProvider is not null)
        {
            return _innerProvider;
        }

        lock (_gate)
        {
            _innerProvider ??= _factory();
            return _innerProvider;
        }
    }
}
