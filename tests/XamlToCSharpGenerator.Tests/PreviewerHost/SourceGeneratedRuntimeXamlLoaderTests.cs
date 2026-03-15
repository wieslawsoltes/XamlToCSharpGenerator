using System.Text;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using XamlToCSharpGenerator.Previewer.DesignerHost;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class SourceGeneratedRuntimeXamlLoaderTests
{
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
