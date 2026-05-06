using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class BrowserLanguageServiceTests
{
    [Fact]
    public async Task BrowserCompilationProvider_ReturnsProjectlessSnapshot()
    {
        using var provider = new BrowserCompilationProvider();

        var snapshot = await provider.GetCompilationAsync(
            "/workspace/MainView.axaml",
            "/workspace",
            CancellationToken.None);

        Assert.Equal("/workspace", snapshot.ProjectPath);
        Assert.Null(snapshot.Project);
        Assert.Null(snapshot.Compilation);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public async Task BrowserEngine_AnalyzesOpenDocumentWithoutMsBuild()
    {
        using var engine = XamlLanguageServiceEngine.CreateBrowser();
        const string uri = "file:///workspace/MainView.axaml";
        const string text = """
                            <UserControl xmlns="https://github.com/avaloniaui">
                              <Button>
                            </UserControl>
                            """;

        var diagnostics = await engine.OpenDocumentAsync(
            uri,
            text,
            version: 1,
            XamlLanguageServiceOptions.Default,
            CancellationToken.None);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Source == "AXSG.Parse");
    }
}
