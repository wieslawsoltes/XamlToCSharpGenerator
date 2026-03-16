using System.Text;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using XamlToCSharpGenerator.Previewer.DesignerHost;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class SourceGeneratedRuntimeXamlLoaderTests
{
    [Fact]
    public void LoadCore_Reuses_Initial_Baseline_For_Successful_Live_Overlay()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var document = new RuntimeXamlLoaderDocument("<UserControl />");
        var configuration = new RuntimeXamlLoaderConfiguration();
        var baseline = new Border();
        var loadCount = 0;
        object? overlayBaseline = null;
        var overlaidRoot = new Border();

        SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
        try
        {
            var result = loader.LoadCore(
                document,
                configuration,
                "<UserControl />",
                sourceFilePath: null,
                assemblyPath: null,
                (_, _, _) =>
                {
                    loadCount++;
                    return baseline;
                },
                (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object baselineRoot, string _, out object overlayResult) =>
                {
                    overlayBaseline = baselineRoot;
                    overlayResult = overlaidRoot;
                    return true;
                });

            Assert.Equal(1, loadCount);
            Assert.Same(baseline, overlayBaseline);
            Assert.Same(overlaidRoot, result);
        }
        finally
        {
            SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
        }
    }

    [Fact]
    public void LoadCore_Clears_Stale_Last_Good_Overlay_When_Baseline_Is_Current()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";
            var document = new RuntimeXamlLoaderDocument(xamlText)
            {
                Document = "View.axaml"
            };
            var configuration = new RuntimeXamlLoaderConfiguration();

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
            SourceGeneratedRuntimeXamlLoader.SetLastGoodOverlayForTests(document, sourceFilePath, "<UserControl Width=\"120\" />");

            var result = loader.LoadCore(
                document,
                configuration,
                xamlText,
                sourceFilePath,
                assemblyPath,
                (_, _, _) => new Border(),
                (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object _, string _, out object overlayResult) =>
                {
                    overlayResult = new Border();
                    return false;
                });

            Assert.IsType<Border>(result);
            Assert.False(SourceGeneratedRuntimeXamlLoader.TryGetLastGoodOverlayForTests(document, sourceFilePath, out _));
        }
        finally
        {
            SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ReadXamlText_Preserves_Seekable_Stream_Position()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<UserControl />"));
        stream.Position = 4;
        var document = new RuntimeXamlLoaderDocument(stream);

        var actual = SourceGeneratedRuntimeXamlLoader.ReadXamlText(document);

        Assert.Equal("<UserControl />", actual);
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public void ShouldApplyLiveOverlay_Returns_False_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.False(SourceGeneratedRuntimeXamlLoader.ShouldApplyLiveOverlay(
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyLiveOverlay_Returns_True_When_Text_Differs_From_Persisted_File()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");

            File.WriteAllText(sourceFilePath, "<UserControl Text=\"Saved\" />");
            File.WriteAllText(assemblyPath, string.Empty);

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyLiveOverlay(
                "<UserControl Text=\"Dirty\" />",
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyLiveOverlay_Returns_True_When_Source_File_Is_Newer_Than_Assembly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now);
            File.SetLastWriteTimeUtc(assemblyPath, now.AddMinutes(-1));

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyLiveOverlay(
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyPreviewOverlay_Returns_True_For_ResourceDictionary_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "Theme.axaml");
            var assemblyPath = Path.Combine(tempRoot, "Library.dll");
            const string xamlText = "<ResourceDictionary />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyPreviewOverlay(
                new ResourceDictionary(),
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyPreviewOverlay_Returns_False_For_Control_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.False(SourceGeneratedRuntimeXamlLoader.ShouldApplyPreviewOverlay(
                new Border(),
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyPreviewOverlay_Returns_True_For_Application_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "App.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<Application />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyPreviewOverlay(
                new Application(),
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
