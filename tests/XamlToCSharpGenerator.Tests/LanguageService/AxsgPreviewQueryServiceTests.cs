using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Remote;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class AxsgPreviewQueryServiceTests
{
    [Fact]
    public async Task GetPreviewProjectContextAsync_Resolves_Project_And_TargetPath()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var projectDirectory = Path.Combine(workspaceRoot, "App");
            Directory.CreateDirectory(projectDirectory);

            var projectPath = Path.Combine(projectDirectory, "App.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var xamlPath = Path.Combine(projectDirectory, "Views", "MainView.axaml");
            Directory.CreateDirectory(Path.GetDirectoryName(xamlPath)!);
            await File.WriteAllTextAsync(xamlPath, "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

            using var engine = new XamlLanguageServiceEngine();
            var service = new AxsgPreviewQueryService(engine, new XamlLanguageServiceOptions(workspaceRoot));

            var context = await service.GetPreviewProjectContextAsync(
                new Uri(xamlPath).AbsoluteUri,
                workspaceRoot: null,
                CancellationToken.None);

            Assert.NotNull(context);
            Assert.Equal(projectPath, context.ProjectPath);
            Assert.Equal("Views/MainView.axaml", context.TargetPath);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "axsg-preview-query-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup.
        }
    }
}
