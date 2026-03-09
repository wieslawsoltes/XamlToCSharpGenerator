using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class DeferredCompilationProviderTests
{
    [Fact]
    public void Does_Not_Create_Inner_Provider_Until_First_Compilation_Request()
    {
        var created = false;
        using var provider = new DeferredCompilationProvider(() =>
        {
            created = true;
            return new FakeCompilationProvider();
        });

        Assert.False(created);
        Assert.False(provider.IsInitialized);
    }

    [Fact]
    public void Invalidate_Does_Not_Create_Inner_Provider()
    {
        var created = false;
        using var provider = new DeferredCompilationProvider(() =>
        {
            created = true;
            return new FakeCompilationProvider();
        });

        provider.Invalidate("/tmp/view.axaml");

        Assert.False(created);
        Assert.False(provider.IsInitialized);
    }

    [Fact]
    public async Task First_Compilation_Request_Creates_Inner_Provider_Once()
    {
        var creationCount = 0;
        var inner = new FakeCompilationProvider();
        using var provider = new DeferredCompilationProvider(() =>
        {
            creationCount++;
            return inner;
        });

        var snapshot = await provider.GetCompilationAsync("/tmp/view.axaml", "/tmp", CancellationToken.None);

        Assert.Equal(1, creationCount);
        Assert.True(provider.IsInitialized);
        Assert.Equal(1, inner.GetCompilationCallCount);
        Assert.Null(snapshot.ProjectPath);
    }

    [Fact]
    public async Task Dispose_Propagates_To_Inner_Provider_When_Initialized()
    {
        var inner = new FakeCompilationProvider();
        var provider = new DeferredCompilationProvider(() => inner);
        await provider.GetCompilationAsync("/tmp/view.axaml", "/tmp", CancellationToken.None);

        provider.Dispose();

        Assert.True(inner.WasDisposed);
    }

    private sealed class FakeCompilationProvider : ICompilationProvider
    {
        public int GetCompilationCallCount { get; private set; }

        public bool WasDisposed { get; private set; }

        public Task<CompilationSnapshot> GetCompilationAsync(string filePath, string? workspaceRoot, CancellationToken cancellationToken)
        {
            GetCompilationCallCount++;
            return Task.FromResult(new CompilationSnapshot(
                ProjectPath: null,
                Project: null,
                Compilation: null,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
        }

        public void Invalidate(string filePath)
        {
        }

        public void Dispose()
        {
            WasDisposed = true;
        }
    }
}
