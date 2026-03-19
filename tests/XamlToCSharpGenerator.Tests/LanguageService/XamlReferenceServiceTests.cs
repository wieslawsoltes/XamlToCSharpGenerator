using System;
using System.Collections.Immutable;
using System.IO;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public class XamlReferenceServiceTests
{
    [Fact]
    public void SortReferencesDeterministically_Orders_By_Uri_Then_Full_Range_Then_Declaration()
    {
        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        builder.Add(CreateReference("file:///b.axaml", 10, 2, 10, 8, false));
        builder.Add(CreateReference("file:///a.axaml", 4, 3, 4, 5, false));
        builder.Add(CreateReference("file:///a.axaml", 4, 3, 4, 4, false));
        builder.Add(CreateReference("file:///a.axaml", 4, 3, 4, 4, true));
        builder.Add(CreateReference("file:///a.axaml", 3, 9, 3, 12, false));

        var sorted = XamlReferenceService.SortReferencesDeterministically(builder);

        Assert.Collection(
            sorted,
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
                Assert.Equal(3, item.Range.Start.Line);
                Assert.Equal(12, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.True(item.IsDeclaration);
                Assert.Equal(4, item.Range.Start.Line);
                Assert.Equal(4, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
                Assert.Equal(4, item.Range.Start.Line);
                Assert.Equal(4, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
                Assert.Equal(4, item.Range.Start.Line);
                Assert.Equal(5, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///b.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
            });
    }

    [Fact]
    public void ResolveProjectPath_Finds_Ancestor_Project_File()
    {
        using var workspace = new ProjectResolutionWorkspace();
        var projectDirectory = Path.Combine(workspace.RootPath, "src", "App");
        Directory.CreateDirectory(projectDirectory);

        var projectFilePath = Path.Combine(projectDirectory, "App.csproj");
        File.WriteAllText(projectFilePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var currentFilePath = Path.Combine(projectDirectory, "Views", "MainView.axaml");
        Directory.CreateDirectory(Path.GetDirectoryName(currentFilePath)!);
        File.WriteAllText(currentFilePath, "<UserControl />");

        var resolved = XamlReferenceService.ResolveProjectPath(null, currentFilePath);

        Assert.Equal(Path.GetFullPath(projectFilePath), resolved);
    }

    [Fact]
    public void ResolveProjectPath_Uses_Explicit_Project_Directory()
    {
        using var workspace = new ProjectResolutionWorkspace();
        var projectDirectory = Path.Combine(workspace.RootPath, "src", "App");
        Directory.CreateDirectory(projectDirectory);

        var projectFilePath = Path.Combine(projectDirectory, "App.csproj");
        File.WriteAllText(projectFilePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var currentFilePath = Path.Combine(workspace.RootPath, "Views", "MainView.axaml");
        Directory.CreateDirectory(Path.GetDirectoryName(currentFilePath)!);
        File.WriteAllText(currentFilePath, "<UserControl />");

        var resolved = XamlReferenceService.ResolveProjectPath(projectDirectory, currentFilePath);

        Assert.Equal(Path.GetFullPath(projectFilePath), resolved);
    }

    [Fact]
    public void ResolveProjectXamlFileByTargetPath_Uses_Link_Metadata()
    {
        using var workspace = new ProjectResolutionWorkspace();
        var projectDirectory = Path.Combine(workspace.RootPath, "src", "App");
        var linkedDirectory = Path.Combine(workspace.RootPath, "shared");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(linkedDirectory);

        var projectFilePath = Path.Combine(projectDirectory, "App.csproj");
        var currentFilePath = Path.Combine(projectDirectory, "Views", "MainView.axaml");
        var linkedFilePath = Path.Combine(linkedDirectory, "Fluent.xaml");
        Directory.CreateDirectory(Path.GetDirectoryName(currentFilePath)!);
        File.WriteAllText(currentFilePath, "<UserControl />");
        File.WriteAllText(linkedFilePath, "<Styles />");
        File.WriteAllText(
            projectFilePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="Views/MainView.axaml" />
                <AvaloniaXaml Include="../../shared/Fluent.xaml" Link="Themes/Fluent.xaml" />
              </ItemGroup>
            </Project>
            """);

        var resolved = XamlProjectFileDiscoveryService.TryResolveProjectXamlFileByTargetPath(
            projectFilePath,
            currentFilePath,
            "Themes/Fluent.xaml",
            out var resolvedFilePath);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(linkedFilePath), resolvedFilePath);
    }

    [Fact]
    public void ResolveProjectXamlFileByTargetPath_Expands_Wildcard_Link_Metadata_Tokens()
    {
        using var workspace = new ProjectResolutionWorkspace();
        var projectDirectory = Path.Combine(workspace.RootPath, "src", "App");
        var linkedDirectory = Path.Combine(workspace.RootPath, "shared", "nested");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(linkedDirectory);

        var projectFilePath = Path.Combine(projectDirectory, "App.csproj");
        var currentFilePath = Path.Combine(projectDirectory, "Views", "MainView.axaml");
        var linkedFilePath = Path.Combine(linkedDirectory, "SharedView.axaml");
        Directory.CreateDirectory(Path.GetDirectoryName(currentFilePath)!);
        File.WriteAllText(currentFilePath, "<UserControl />");
        File.WriteAllText(linkedFilePath, "<UserControl />");
        File.WriteAllText(
            projectFilePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="Views/MainView.axaml" />
                <AvaloniaXaml Include="../../shared/**/*.axaml" Link="Views/%(RecursiveDir)%(Filename)%(Extension)" />
              </ItemGroup>
            </Project>
            """);

        var resolved = XamlProjectFileDiscoveryService.TryResolveProjectXamlFileByTargetPath(
            projectFilePath,
            currentFilePath,
            "Views/nested/SharedView.axaml",
            out var resolvedFilePath);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(linkedFilePath), resolvedFilePath);
    }

    [Fact]
    public void ResolveProjectXamlEntryByFilePath_Returns_TargetPath()
    {
        using var workspace = new ProjectResolutionWorkspace();
        var projectDirectory = Path.Combine(workspace.RootPath, "src", "App");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));

        var projectFilePath = Path.Combine(projectDirectory, "App.csproj");
        var currentFilePath = Path.Combine(projectDirectory, "Views", "MainView.axaml");
        File.WriteAllText(
            projectFilePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="Views/MainView.axaml" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(currentFilePath, "<UserControl />");

        var resolved = XamlProjectFileDiscoveryService.TryResolveProjectXamlEntryByFilePath(
            projectFilePath,
            currentFilePath,
            currentFilePath,
            out var entry);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(currentFilePath), entry.FilePath);
        Assert.Equal("Views/MainView.axaml", entry.TargetPath);
    }

    [Fact]
    public void TryResolveOwningProjectXamlEntry_Finds_Linked_File_Outside_Project_Directory()
    {
        using var workspace = new ProjectResolutionWorkspace();
        var unrelatedProjectDirectory = Path.Combine(workspace.RootPath, "src", "Aardvark");
        var libraryDirectory = Path.Combine(workspace.RootPath, "src", "PreviewLibrary");
        var sharedDirectory = Path.Combine(workspace.RootPath, "shared");
        Directory.CreateDirectory(unrelatedProjectDirectory);
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(sharedDirectory);

        File.WriteAllText(
            Path.Combine(unrelatedProjectDirectory, "Aardvark.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var libraryProjectPath = Path.Combine(libraryDirectory, "PreviewLibrary.csproj");
        var sharedFilePath = Path.Combine(sharedDirectory, "SharedView.axaml");
        File.WriteAllText(sharedFilePath, "<UserControl />");
        File.WriteAllText(
            libraryProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="../../shared/SharedView.axaml" Link="Views/SharedView.axaml" />
              </ItemGroup>
            </Project>
            """);

        var resolved = XamlProjectFileDiscoveryService.TryResolveOwningProjectXamlEntry(
            sharedFilePath,
            workspace.RootPath,
            out var projectPath,
            out var entry);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(libraryProjectPath), projectPath);
        Assert.Equal(Path.GetFullPath(sharedFilePath), entry.FilePath);
        Assert.Equal("Views/SharedView.axaml", entry.TargetPath);
    }

    [Fact]
    public void ReusesParsedXmlForStaleSourceSnapshot()
    {
        XamlReferenceService.ClearCachesForTesting();
        using var workspace = new ProjectResolutionWorkspace();
        var filePath = Path.Combine(workspace.RootPath, "Views", "MainView.axaml");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "<UserControl><TextBlock Text=\"Hello\" /></UserControl>");

        var reused = XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(filePath);

        Assert.True(reused);
        Assert.True(XamlReferenceService.TryGetCachedSourceState(filePath, out var xmlParsed, out var hasXmlDocument));
        Assert.True(xmlParsed);
        Assert.True(hasXmlDocument);
    }

    [Fact]
    public void ProjectSourceSnapshot_Does_Not_Retain_Strong_Xml_Documents()
    {
        XamlReferenceService.ClearCachesForTesting();
        using var workspace = new ProjectResolutionWorkspace();
        var projectDirectory = Path.Combine(workspace.RootPath, "src", "App");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));

        var projectFilePath = Path.Combine(projectDirectory, "App.csproj");
        File.WriteAllText(
            projectFilePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="Views/**/*.axaml" />
              </ItemGroup>
            </Project>
            """);

        var firstFilePath = Path.Combine(projectDirectory, "Views", "MainView.axaml");
        var secondFilePath = Path.Combine(projectDirectory, "Views", "DetailsView.axaml");
        File.WriteAllText(firstFilePath, "<UserControl><TextBlock Text=\"Hello\" /></UserControl>");
        File.WriteAllText(secondFilePath, "<UserControl><Border /></UserControl>");

        Assert.True(XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(firstFilePath));
        Assert.True(XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(secondFilePath));

        Assert.Equal(0, XamlReferenceService.CountRetainedProjectSnapshotXmlDocumentsForTesting(projectFilePath));
    }

    [Fact]
    public void Cached_Xml_Documents_Can_Be_Reclaimed_And_Reparsed()
    {
        XamlReferenceService.ClearCachesForTesting();
        using var workspace = new ProjectResolutionWorkspace();
        var filePath = Path.Combine(workspace.RootPath, "Views", "MainView.axaml");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(
            filePath,
            """
            <UserControl xmlns="https://github.com/avaloniaui">
              <StackPanel>
                <TextBlock Text="Hello" />
                <Border />
              </StackPanel>
            </UserControl>
            """);

        Assert.True(XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(filePath));
        Assert.True(XamlReferenceService.TryGetCachedSourceState(filePath, out var xmlParsedBeforeGc, out var hasXmlDocumentBeforeGc));
        Assert.True(xmlParsedBeforeGc);
        Assert.True(hasXmlDocumentBeforeGc);

        ForceCollectionUntil(() =>
        {
            Assert.True(XamlReferenceService.TryGetCachedSourceState(filePath, out var xmlParsed, out var hasXmlDocument));
            return xmlParsed && !hasXmlDocument;
        });

        Assert.True(XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(filePath));
        Assert.True(XamlReferenceService.TryGetCachedSourceState(filePath, out var xmlParsedAfterReload, out var hasXmlDocumentAfterReload));
        Assert.True(xmlParsedAfterReload);
        Assert.True(hasXmlDocumentAfterReload);
    }

    private static void ForceCollectionUntil(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (predicate())
            {
                return;
            }

            var pressure = new byte[1024 * 1024 * 8];
            GC.KeepAlive(pressure);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.True(predicate());
    }

    private static XamlReferenceLocation CreateReference(
        string uri,
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        bool isDeclaration)
    {
        return new XamlReferenceLocation(
            uri,
            new SourceRange(
                new SourcePosition(startLine, startCharacter),
                new SourcePosition(endLine, endCharacter)),
            isDeclaration);
    }

    private sealed class ProjectResolutionWorkspace : IDisposable
    {
        public ProjectResolutionWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "axsg-project-resolution-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
