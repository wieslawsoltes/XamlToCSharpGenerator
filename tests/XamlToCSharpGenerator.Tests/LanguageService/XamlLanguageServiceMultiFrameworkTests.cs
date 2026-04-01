using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Framework;
using XamlToCSharpGenerator.LanguageService.Framework.All;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class XamlLanguageServiceMultiFrameworkTests
{
    private const string PresentationXmlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string MauiXmlNamespace = "http://schemas.microsoft.com/dotnet/2021/maui";
    private const string CustomXmlNamespace = "https://example.com/custom";

    [Fact]
    public void BuiltInRegistry_ContainsAllBuiltInFrameworks()
    {
        var registry = XamlBuiltInLanguageFrameworkRegistry.Create();

        Assert.Equal(4, registry.Providers.Length);
        Assert.True(registry.TryGetById(FrameworkProfileIds.Avalonia, out _));
        Assert.True(registry.TryGetById(FrameworkProfileIds.Wpf, out _));
        Assert.True(registry.TryGetById(FrameworkProfileIds.WinUI, out _));
        Assert.True(registry.TryGetById(FrameworkProfileIds.Maui, out _));
    }

    [Fact]
    public async Task AnalyzeAsync_InfersWpfFrameworkFromCompilationMarkers()
    {
        var analysis = await AnalyzeAsync(
            CreateWpfCompilation(),
            """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """,
            "/tmp/MainWindow.xaml");

        Assert.Equal(FrameworkProfileIds.Wpf, analysis.Framework.Id);
        Assert.Equal(PresentationXmlNamespace, analysis.Framework.DefaultXmlNamespace);
        Assert.NotNull(analysis.TypeIndex);
        Assert.True(analysis.TypeIndex!.TryGetType(PresentationXmlNamespace, "Window", out var typeInfo));
        Assert.Equal("System.Windows.Controls.Window", typeInfo!.FullTypeName);
    }

    [Fact]
    public async Task AnalyzeAsync_InfersWinUiFrameworkFromCompilationMarkers()
    {
        var analysis = await AnalyzeAsync(
            CreateWinUiCompilation(),
            """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """,
            "/tmp/MainPage.xaml");

        Assert.Equal(FrameworkProfileIds.WinUI, analysis.Framework.Id);
        Assert.Equal(PresentationXmlNamespace, analysis.Framework.DefaultXmlNamespace);
        Assert.NotNull(analysis.TypeIndex);
        Assert.True(analysis.TypeIndex!.TryGetType(PresentationXmlNamespace, "Page", out var typeInfo));
        Assert.Equal("Microsoft.UI.Xaml.Controls.Page", typeInfo!.FullTypeName);
    }

    [Fact]
    public async Task AnalyzeAsync_InfersMauiFrameworkFromCompilationMarkers()
    {
        var analysis = await AnalyzeAsync(
            CreateMauiCompilation(),
            """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui" />
            """,
            "/tmp/MainPage.xaml");

        Assert.Equal(FrameworkProfileIds.Maui, analysis.Framework.Id);
        Assert.Equal(MauiXmlNamespace, analysis.Framework.DefaultXmlNamespace);
        Assert.NotNull(analysis.TypeIndex);
        Assert.True(analysis.TypeIndex!.TryGetType(MauiXmlNamespace, "ContentPage", out var typeInfo));
        Assert.Equal("Microsoft.Maui.Controls.ContentPage", typeInfo!.FullTypeName);
    }

    [Fact]
    public async Task Completion_Wpf_ExcludesAvaloniaSpecificDirectivesAndMarkupExtensions()
    {
        using var engine = new XamlLanguageServiceEngine(new InMemoryCompilationProvider(CreateWpfCompilation()));
        const string uri = "file:///tmp/WpfCompletion.xaml";
        const string xaml = "<Window ";
        var options = CreateOptions(frameworkId: FrameworkProfileIds.Wpf);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);
        var attributeCompletions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(0, xaml.Length),
            options,
            CancellationToken.None);

        Assert.Contains(attributeCompletions, item => string.Equals(item.Label, "Title", StringComparison.Ordinal));
        Assert.DoesNotContain(attributeCompletions, item => string.Equals(item.Label, "x:CompileBindings", StringComparison.Ordinal));

        const string markupXaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Title=\"{\" />";
        await engine.UpdateDocumentAsync(uri, markupXaml, version: 2, options, CancellationToken.None);
        var markupCaret = SourceText.From(markupXaml).Lines.GetLinePosition(markupXaml.IndexOf("{", StringComparison.Ordinal) + 1);
        var markupCompletions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(markupCaret.Line, markupCaret.Character),
            options,
            CancellationToken.None);

        Assert.Contains(markupCompletions, item => string.Equals(item.Label, "Binding", StringComparison.Ordinal));
        Assert.DoesNotContain(markupCompletions, item => string.Equals(item.Label, "CompiledBinding", StringComparison.Ordinal));
        Assert.DoesNotContain(markupCompletions, item => string.Equals(item.Label, "x:Bind", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_WinUi_OffersXBind()
    {
        using var engine = new XamlLanguageServiceEngine(new InMemoryCompilationProvider(CreateWinUiCompilation()));
        const string uri = "file:///tmp/WinUiCompletion.xaml";
        const string xaml = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Title=\"{\" />";
        var options = CreateOptions(frameworkId: FrameworkProfileIds.WinUI);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{", StringComparison.Ordinal) + 1);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(caret.Line, caret.Character),
            options,
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "x:Bind", StringComparison.Ordinal));
        Assert.DoesNotContain(completions, item => string.Equals(item.Label, "CompiledBinding", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Maui_DefaultNamespaceOffersMauiControls()
    {
        using var engine = new XamlLanguageServiceEngine(new InMemoryCompilationProvider(CreateMauiCompilation()));
        const string uri = "file:///tmp/MauiCompletion.xaml";
        const string xaml =
            "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\">\n" +
            "  <La\n" +
            "</ContentPage>";
        var options = CreateOptions(frameworkId: FrameworkProfileIds.Maui);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);
        var caret = GetPosition(xaml, xaml.IndexOf("<La", StringComparison.Ordinal) + "<La".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            caret,
            options,
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "Label", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definition_WpfPackUri_ResolvesProjectFile()
    {
        using var workspace = new TempWorkspace();
        var projectPath = Path.Combine(workspace.RootPath, "App.csproj");
        var dictionariesDirectory = Path.Combine(workspace.RootPath, "Themes");
        Directory.CreateDirectory(dictionariesDirectory);

        var hostFilePath = Path.Combine(dictionariesDirectory, "AppResources.xaml");
        var targetFilePath = Path.Combine(dictionariesDirectory, "Colors.xaml");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UseWPF>true</UseWPF>
              </PropertyGroup>
              <ItemGroup>
                <Page Include="Themes/AppResources.xaml" />
                <Page Include="Themes/Colors.xaml" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            hostFilePath,
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                Source="pack://application:,,,/Themes/Colors.xaml" />
            """);
        File.WriteAllText(
            targetFilePath,
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """);
        var hostText = File.ReadAllText(hostFilePath);

        using var engine = new XamlLanguageServiceEngine(new InMemoryCompilationProvider(CreateWpfCompilation()));
        var hostUri = UriPathHelper.ToDocumentUri(hostFilePath);
        var options = CreateOptions(projectPath, FrameworkProfileIds.Wpf);
        await engine.OpenDocumentAsync(hostUri, hostText, version: 1, options, CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            hostUri,
            GetPosition(hostText, hostText.IndexOf("Colors.xaml", StringComparison.Ordinal) + 2),
            options,
            CancellationToken.None);

        var definition = Assert.Single(definitions);
        Assert.Equal(UriPathHelper.ToDocumentUri(targetFilePath), definition.Uri);
    }

    [Fact]
    public async Task Definition_WinUiMsAppxUri_ResolvesProjectFile()
    {
        using var workspace = new TempWorkspace();
        var projectPath = Path.Combine(workspace.RootPath, "App.csproj");
        var dictionariesDirectory = Path.Combine(workspace.RootPath, "Themes");
        Directory.CreateDirectory(dictionariesDirectory);

        var hostFilePath = Path.Combine(dictionariesDirectory, "AppResources.xaml");
        var targetFilePath = Path.Combine(dictionariesDirectory, "Colors.xaml");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UseWinUI>true</UseWinUI>
              </PropertyGroup>
              <ItemGroup>
                <Page Include="Themes/AppResources.xaml" />
                <Page Include="Themes/Colors.xaml" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            hostFilePath,
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                Source="ms-appx:///Themes/Colors.xaml" />
            """);
        File.WriteAllText(
            targetFilePath,
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """);
        var hostText = File.ReadAllText(hostFilePath);

        using var engine = new XamlLanguageServiceEngine(new InMemoryCompilationProvider(CreateWinUiCompilation()));
        var hostUri = UriPathHelper.ToDocumentUri(hostFilePath);
        var options = CreateOptions(projectPath, FrameworkProfileIds.WinUI);
        await engine.OpenDocumentAsync(hostUri, hostText, version: 1, options, CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            hostUri,
            GetPosition(hostText, hostText.IndexOf("Colors.xaml", StringComparison.Ordinal) + 2),
            options,
            CancellationToken.None);

        var definition = Assert.Single(definitions);
        Assert.Equal(UriPathHelper.ToDocumentUri(targetFilePath), definition.Uri);
    }

    [Fact]
    public void ProjectDiscovery_RecognizesMauiXamlItems()
    {
        using var workspace = new TempWorkspace();
        var projectPath = Path.Combine(workspace.RootPath, "App.csproj");
        var pagesDirectory = Path.Combine(workspace.RootPath, "Pages");
        Directory.CreateDirectory(pagesDirectory);

        var pageFilePath = Path.Combine(pagesDirectory, "MainPage.xaml");
        File.WriteAllText(pageFilePath, "<ContentPage />");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UseMaui>true</UseMaui>
              </PropertyGroup>
              <ItemGroup>
                <MauiXaml Include="Pages/MainPage.xaml" />
              </ItemGroup>
            </Project>
            """);

        var resolved = XamlProjectFileDiscoveryService.TryResolveProjectXamlEntryByFilePath(
            projectPath,
            pageFilePath,
            pageFilePath,
            out var entry);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(pageFilePath), entry.FilePath);
        Assert.Equal("Pages/MainPage.xaml", entry.TargetPath);
    }

    [Fact]
    public async Task AnalyzeAsync_RecomputesFrameworkWhenProjectFileChanges()
    {
        using var workspace = new TempWorkspace();
        var projectPath = Path.Combine(workspace.RootPath, "App.csproj");
        var documentPath = Path.Combine(workspace.RootPath, "MainView.xaml");
        const string xaml = "<Page />";

        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UseWPF>true</UseWPF>
              </PropertyGroup>
            </Project>
            """);
        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddSeconds(-2));

        var analysisService = new XamlCompilerAnalysisService(new InMemoryCompilationProvider(CreateCompilation(
            """
            namespace App;

            public sealed class Placeholder
            {
            }
            """,
            "ProjectFrameworkRecomputeTests")));
        var document = new LanguageServiceDocument(
            UriPathHelper.ToDocumentUri(documentPath),
            documentPath,
            xaml,
            Version: 1);

        var wpfAnalysis = await analysisService.AnalyzeAsync(
            document,
            CreateOptions(projectPath),
            CancellationToken.None);

        Assert.Equal(FrameworkProfileIds.Wpf, wpfAnalysis.Framework.Id);

        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UseMaui>true</UseMaui>
              </PropertyGroup>
              <ItemGroup>
                <MauiXaml Include="MainView.xaml" />
              </ItemGroup>
            </Project>
            """);
        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddSeconds(2));

        var mauiAnalysis = await analysisService.AnalyzeAsync(
            document,
            CreateOptions(projectPath),
            CancellationToken.None);

        Assert.Equal(FrameworkProfileIds.Maui, mauiAnalysis.Framework.Id);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesCustomRegistryForFrameworkResolutionAndProjectDiscovery()
    {
        using var workspace = new TempWorkspace();
        var projectPath = Path.Combine(workspace.RootPath, "App.csproj");
        var viewDirectory = Path.Combine(workspace.RootPath, "Views");
        Directory.CreateDirectory(viewDirectory);

        var documentPath = Path.Combine(viewDirectory, "MainView.xaml");
        const string xaml = "<RootView xmlns=\"https://example.com/custom\" />";

        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <CustomXaml Include="Views/MainView.xaml" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(documentPath, xaml);

        var registry = new XamlLanguageFrameworkRegistryBuilder()
            .Add(CustomLanguageFrameworkProvider.Instance)
            .Build(CustomLanguageFrameworkProvider.FrameworkId);
        var analysisService = new XamlCompilerAnalysisService(
            new InMemoryCompilationProvider(CreateCustomCompilation()),
            registry);
        var document = new LanguageServiceDocument(
            UriPathHelper.ToDocumentUri(documentPath),
            documentPath,
            xaml,
            Version: 1);

        var analysis = await analysisService.AnalyzeAsync(
            document,
            CreateOptions(projectPath),
            CancellationToken.None);

        Assert.Equal(CustomLanguageFrameworkProvider.FrameworkId, analysis.Framework.Id);
        Assert.True(XamlProjectFileDiscoveryService.TryResolveProjectXamlEntryByFilePath(
            projectPath,
            documentPath,
            documentPath,
            registry,
            out var entry));
        Assert.Equal("Views/MainView.xaml", entry.TargetPath);
    }

    [Fact]
    public async Task Completion_CustomRegistry_UsesProviderSpecificMarkupExtensions()
    {
        var registry = new XamlLanguageFrameworkRegistryBuilder()
            .Add(CustomLanguageFrameworkProvider.Instance)
            .Build(CustomLanguageFrameworkProvider.FrameworkId);
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(CreateCustomCompilation()),
            registry);

        const string uri = "file:///tmp/CustomCompletion.xaml";
        const string xaml = "<RootView xmlns=\"https://example.com/custom\" Title=\"{\" />";
        var options = CreateOptions(frameworkId: CustomLanguageFrameworkProvider.FrameworkId);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{", StringComparison.Ordinal) + 1);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(caret.Line, caret.Character),
            options,
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "CustomMarkup", StringComparison.Ordinal));
        Assert.DoesNotContain(completions, item => string.Equals(item.Label, "CompiledBinding", StringComparison.Ordinal));
    }

    private static async Task<XamlAnalysisResult> AnalyzeAsync(Compilation compilation, string xaml, string filePath)
    {
        var analysisService = new XamlCompilerAnalysisService(new InMemoryCompilationProvider(compilation));
        var document = new LanguageServiceDocument(
            UriPathHelper.ToDocumentUri(filePath),
            filePath,
            xaml,
            Version: 1);

        return await analysisService.AnalyzeAsync(
            document,
            CreateOptions(),
            CancellationToken.None);
    }

    private static XamlLanguageServiceOptions CreateOptions(string? workspaceRoot = null, string? frameworkId = null)
    {
        return new XamlLanguageServiceOptions(
            WorkspaceRoot: workspaceRoot,
            FrameworkId: frameworkId,
            IncludeCompilationDiagnostics: false,
            IncludeSemanticDiagnostics: false);
    }

    private static SourcePosition GetPosition(string text, int offset)
    {
        var position = SourceText.From(text).Lines.GetLinePosition(offset);
        return new SourcePosition(position.Line, position.Character);
    }

    private static CSharpCompilation CreateWpfCompilation()
    {
        const string source = """
                              using System;

                              [assembly: System.Windows.Markup.XmlnsDefinitionAttribute("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "System.Windows")]
                              [assembly: System.Windows.Markup.XmlnsDefinitionAttribute("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "System.Windows.Controls")]

                              namespace System.Windows.Markup
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }

                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsPrefixAttribute : Attribute
                                  {
                                      public XmlnsPrefixAttribute(string xmlNamespace, string prefix) { }
                                  }
                              }

                              namespace System.Windows
                              {
                                  public class Application { }

                                  public class ResourceDictionary
                                  {
                                      public string? Source { get; set; }
                                  }
                              }

                              namespace System.Windows.Controls
                              {
                                  public class Window
                                  {
                                      public object? Content { get; set; }
                                      public string? Title { get; set; }
                                  }

                                  public class TextBlock
                                  {
                                      public string? Text { get; set; }
                                  }
                              }
                              """;

        return CreateCompilation(source, "WpfLanguageServiceTests");
    }

    private static CSharpCompilation CreateWinUiCompilation()
    {
        const string source = """
                              using System;

                              [assembly: Microsoft.UI.Xaml.Markup.XmlnsDefinitionAttribute("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "Microsoft.UI.Xaml.Controls")]

                              namespace Microsoft.UI.Xaml.Markup
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }

                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsPrefixAttribute : Attribute
                                  {
                                      public XmlnsPrefixAttribute(string xmlNamespace, string prefix) { }
                                  }
                              }

                              namespace Microsoft.UI.Xaml
                              {
                                  public class Application { }
                              }

                              namespace Microsoft.UI.Xaml.Controls
                              {
                                  public class Page
                                  {
                                      public object? Content { get; set; }
                                      public string? Title { get; set; }
                                  }

                                  public class TextBlock
                                  {
                                      public string? Text { get; set; }
                                  }
                              }
                              """;

        return CreateCompilation(source, "WinUiLanguageServiceTests");
    }

    private static CSharpCompilation CreateMauiCompilation()
    {
        const string source = """
                              using System;

                              [assembly: Microsoft.Maui.Controls.Xaml.XmlnsDefinitionAttribute("http://schemas.microsoft.com/dotnet/2021/maui", "Microsoft.Maui.Controls")]

                              namespace Microsoft.Maui.Controls.Xaml
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }

                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsPrefixAttribute : Attribute
                                  {
                                      public XmlnsPrefixAttribute(string xmlNamespace, string prefix) { }
                                  }
                              }

                              namespace Microsoft.Maui.Controls
                              {
                                  public class Application { }

                                  public class ContentPage
                                  {
                                      public object? Content { get; set; }
                                      public string? Title { get; set; }
                                  }

                                  public class Label
                                  {
                                      public string? Text { get; set; }
                                  }
                              }
                              """;

        return CreateCompilation(source, "MauiLanguageServiceTests");
    }

    private static CSharpCompilation CreateCustomCompilation()
    {
        const string source = """
                              using System;

                              [assembly: System.Windows.Markup.XmlnsDefinitionAttribute("https://example.com/custom", "Custom.Framework")]

                              namespace System.Windows.Markup
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }

                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsPrefixAttribute : Attribute
                                  {
                                      public XmlnsPrefixAttribute(string xmlNamespace, string prefix) { }
                                  }
                              }

                              namespace Custom.Framework
                              {
                                  public class RootView
                                  {
                                      public string? Title { get; set; }
                                  }
                              }
                              """;

        return CreateCompilation(source, "CustomLanguageServiceTests");
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
    {
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "axsg-multiframework-" + Guid.NewGuid().ToString("N"));
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

    private sealed class CustomLanguageFrameworkProvider : IXamlLanguageFrameworkProvider
    {
        public const string FrameworkId = "custom";

        public static CustomLanguageFrameworkProvider Instance { get; } = new();

        private CustomLanguageFrameworkProvider()
        {
            Framework = new XamlLanguageFrameworkInfo(
                Id: FrameworkId,
                Profile: new PassiveXamlFrameworkProfile(
                    FrameworkId,
                    CustomXmlNamespace,
                    preferredProjectXamlItemName: "CustomXaml",
                    projectXamlItemNames:
                    [
                        "CustomXaml"
                    ]),
                DefaultXmlNamespace: CustomXmlNamespace,
                XmlnsDefinitionAttributeMetadataNames:
                [
                    "System.Windows.Markup.XmlnsDefinitionAttribute"
                ],
                XmlnsPrefixAttributeMetadataNames:
                [
                    "System.Windows.Markup.XmlnsPrefixAttribute"
                ],
                MarkupExtensionNamespaces:
                [
                    "System.Windows.Markup"
                ],
                PreferredProjectXamlItemName: "CustomXaml",
                ProjectXamlItemNames:
                [
                    "CustomXaml"
                ],
                DirectiveCompletions:
                [
                    XamlLanguageFrameworkCompletion.Create("x:CustomData", "x:CustomData=\"$0\"", "Custom framework directive")
                ],
                MarkupExtensionCompletions:
                [
                    XamlLanguageFrameworkCompletion.Create("CustomMarkup", "{CustomMarkup $0}", "Custom framework markup extension")
                ]);
        }

        public XamlLanguageFrameworkInfo Framework { get; }

        public int DetectionPriority => 900;

        public bool CanResolveFromProject(System.Xml.Linq.XDocument projectDocument, string projectPath)
        {
            _ = projectPath;
            return XamlLanguageFrameworkDetectionHelpers.HasItemElement(projectDocument, "CustomXaml");
        }

        public bool CanResolveFromCompilation(Compilation compilation)
        {
            return XamlLanguageFrameworkDetectionHelpers.HasType(compilation, "Custom.Framework.RootView");
        }

        public bool CanResolveFromDocument(string filePath, string? documentText)
        {
            _ = filePath;
            return XamlLanguageFrameworkDetectionHelpers.DocumentContains(documentText, CustomXmlNamespace);
        }
    }
}
