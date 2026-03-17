using System;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;
using XamlToCSharpGenerator.McpServer.Server;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class McpServerIntegrationTests
{
    [Fact]
    public async Task Initialize_Then_ListTools_Returns_Unified_Tool_Surface()
    {
        await using var harness = await McpServerHarness.StartAsync();

        await harness.InitializeAsync();
        await harness.SendRequestAsync(1, "tools/list", new JsonObject());

        using var response = await harness.ReadResponseAsync(1);
        var tools = response.RootElement.GetProperty("result").GetProperty("tools");
        var toolNames = tools.EnumerateArray()
            .Select(static tool => tool.GetProperty("name").GetString())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.preview.projectContext", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.hotReload.status", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.hotReload.trackedDocuments", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.hotDesign.workspace", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.studio.status", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.metadataDocument", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.inlineCSharpProjections", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.csharpReferences", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.csharpDeclarations", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.renamePropagation", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.prepareRename", StringComparison.Ordinal));
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.workspace.rename", StringComparison.Ordinal));
        Assert.DoesNotContain(toolNames, static name => string.Equals(name, "axsg.hotReload.enable", StringComparison.Ordinal));
        Assert.DoesNotContain(toolNames, static name => string.Equals(name, "axsg.hotDesign.enable", StringComparison.Ordinal));
        Assert.DoesNotContain(toolNames, static name => string.Equals(name, "axsg.studio.enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolCall_HotReloadStatus_Returns_Structured_Result()
    {
        await using var harness = await McpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(2, "tools/call", new JsonObject
        {
            ["name"] = "axsg.hotReload.status",
            ["arguments"] = new JsonObject()
        });

        using var response = await harness.ReadResponseAsync(2);
        var result = response.RootElement.GetProperty("result");

        Assert.False(result.GetProperty("isError").GetBoolean());
        var structuredContent = result.GetProperty("structuredContent");
        Assert.True(structuredContent.GetProperty("registeredTypeCount").GetInt32() >= 0);
    }

    [Fact]
    public async Task ToolCall_PreviewProjectContext_Resolves_Project_And_TargetPath()
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

            await using var harness = await McpServerHarness.StartAsync(workspaceRoot);
            await harness.InitializeAsync();

            await harness.SendRequestAsync(3, "tools/call", new JsonObject
            {
                ["name"] = "axsg.preview.projectContext",
                ["arguments"] = new JsonObject
                {
                    ["uri"] = new Uri(xamlPath).AbsoluteUri
                }
            });

            using var response = await harness.ReadResponseAsync(3);
            var structuredContent = response.RootElement.GetProperty("result").GetProperty("structuredContent");

            Assert.Equal(projectPath, structuredContent.GetProperty("projectPath").GetString());
            Assert.Equal("Views/MainView.axaml", structuredContent.GetProperty("targetPath").GetString());
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task ToolCall_WorkspaceInlineCSharpProjections_Returns_Projected_Text()
    {
        await using var harness = await McpServerHarness.StartAsync(
            workspaceRoot: "/tmp",
            compilationProvider: LanguageServiceTestCompilationFactory.CreateHarnessCompilationProvider());
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineProjectionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CSharp Code=source.ProductName}\"/>\n" +
                            "</UserControl>";

        await harness.SendRequestAsync(4, "tools/call", new JsonObject
        {
            ["name"] = "axsg.workspace.inlineCSharpProjections",
            ["arguments"] = new JsonObject
            {
                ["uri"] = uri,
                ["workspaceRoot"] = "/tmp",
                ["documentText"] = xaml,
                ["version"] = 1
            }
        });

        using var response = await harness.ReadResponseAsync(4);
        var projections = response.RootElement.GetProperty("result").GetProperty("structuredContent");

        Assert.True(projections.GetArrayLength() > 0);
        Assert.Contains(projections.EnumerateArray(), item =>
            item.GetProperty("projectedText").GetString()?.Contains("source.ProductName", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ToolCall_WorkspaceMetadataDocument_Resolves_From_MetadataUri()
    {
        const string uri = "file:///tmp/MetadataDocumentView.axaml";
        const string xaml = "<ext:ExternalButton xmlns:ext=\"using:ExtLib.Controls\" />";

        using (var engine = new XamlLanguageServiceEngine(new InMemoryCompilationProvider(CreateCompilationWithExternalControls())))
        {
            await engine.OpenDocumentAsync(
                uri,
                xaml,
                version: 1,
                new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: false, IncludeSemanticDiagnostics: false),
                CancellationToken.None);

            var definitions = await engine.GetDefinitionsAsync(
                uri,
                new SourcePosition(0, xaml.IndexOf("ExternalButton", StringComparison.Ordinal) + 2),
                new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: false, IncludeSemanticDiagnostics: false),
                CancellationToken.None);

            var metadataUri = definitions
                .Select(static definition => definition.Uri)
                .FirstOrDefault(static definitionUri => definitionUri.StartsWith("axsg-metadata:///", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(metadataUri));

            await using var harness = await McpServerHarness.StartAsync(
                workspaceRoot: "/tmp",
                compilationProvider: new InMemoryCompilationProvider(CreateCompilationWithExternalControls()));
            await harness.InitializeAsync();

            await harness.SendRequestAsync(5, "tools/call", new JsonObject
            {
                ["name"] = "axsg.workspace.metadataDocument",
                ["arguments"] = new JsonObject
                {
                    ["metadataUri"] = metadataUri
                }
            });

            using var response = await harness.ReadResponseAsync(5);
            var structuredContent = response.RootElement.GetProperty("result").GetProperty("structuredContent");
            var documentText = structuredContent.GetProperty("text").GetString();

            Assert.NotNull(documentText);
            Assert.Contains("public class ExternalButton", documentText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ToolCall_WorkspaceCSharpReferences_Returns_Xaml_Locations()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();
        await using var harness = await McpServerHarness.StartAsync(fixture.RootPath, new MsBuildCompilationProvider());
        await harness.InitializeAsync();

        await harness.SendRequestAsync(6, "tools/call", new JsonObject
        {
            ["name"] = "axsg.workspace.csharpReferences",
            ["arguments"] = new JsonObject
            {
                ["uri"] = fixture.CodeUri,
                ["line"] = fixture.ViewModelTypePosition.Line,
                ["character"] = fixture.ViewModelTypePosition.Character,
                ["workspaceRoot"] = fixture.RootPath,
                ["documentText"] = fixture.CodeText
            }
        });

        using var response = await harness.ReadResponseAsync(6);
        var references = response.RootElement.GetProperty("result").GetProperty("structuredContent");

        Assert.True(references.GetArrayLength() > 0);
        Assert.Contains(references.EnumerateArray(), item =>
            string.Equals(item.GetProperty("uri").GetString(), fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolCall_WorkspaceCSharpDeclarations_Returns_Xaml_XClass_Location()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();
        await using var harness = await McpServerHarness.StartAsync(fixture.RootPath, new MsBuildCompilationProvider());
        await harness.InitializeAsync();

        await harness.SendRequestAsync(7, "tools/call", new JsonObject
        {
            ["name"] = "axsg.workspace.csharpDeclarations",
            ["arguments"] = new JsonObject
            {
                ["uri"] = fixture.CodeUri,
                ["line"] = fixture.MainViewTypePosition.Line,
                ["character"] = fixture.MainViewTypePosition.Character,
                ["workspaceRoot"] = fixture.RootPath,
                ["documentText"] = fixture.CodeText
            }
        });

        using var response = await harness.ReadResponseAsync(7);
        var declarations = response.RootElement.GetProperty("result").GetProperty("structuredContent");

        Assert.True(declarations.GetArrayLength() > 0);
        Assert.Contains(declarations.EnumerateArray(), item =>
            string.Equals(item.GetProperty("uri").GetString(), fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolCall_WorkspacePrepareRename_Returns_Range_And_Placeholder()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await McpServerHarness.StartAsync(project.RootPath, new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(8, "tools/call", new JsonObject
            {
                ["name"] = "axsg.workspace.prepareRename",
                ["arguments"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["line"] = project.XamlNamePosition.Line,
                    ["character"] = project.XamlNamePosition.Character,
                    ["workspaceRoot"] = project.RootPath,
                    ["documentText"] = project.XamlText
                }
            });

            using var response = await harness.ReadResponseAsync(8);
            var result = response.RootElement.GetProperty("result").GetProperty("structuredContent");

            Assert.Equal("Name", result.GetProperty("placeholder").GetString());
            Assert.True(result.TryGetProperty("range", out _));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ToolCall_WorkspaceRename_Returns_Workspace_Edit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await McpServerHarness.StartAsync(project.RootPath, new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(9, "tools/call", new JsonObject
            {
                ["name"] = "axsg.workspace.rename",
                ["arguments"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["line"] = project.XamlNamePosition.Line,
                    ["character"] = project.XamlNamePosition.Character,
                    ["workspaceRoot"] = project.RootPath,
                    ["documentText"] = project.XamlText,
                    ["newName"] = "DisplayName"
                }
            });

            using var response = await harness.ReadResponseAsync(9);
            var changes = response.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("changes");

            Assert.True(changes.TryGetProperty(project.XamlUri, out _));
            Assert.True(changes.TryGetProperty(project.CodeUri, out _));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ToolCall_WorkspaceRenamePropagation_Returns_Xaml_Only_Edit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await McpServerHarness.StartAsync(project.RootPath, new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(10, "tools/call", new JsonObject
            {
                ["name"] = "axsg.workspace.renamePropagation",
                ["arguments"] = new JsonObject
                {
                    ["uri"] = project.CodeUri,
                    ["line"] = project.CodeNamePosition.Line,
                    ["character"] = project.CodeNamePosition.Character,
                    ["workspaceRoot"] = project.RootPath,
                    ["documentText"] = project.CodeText,
                    ["newName"] = "DisplayName"
                }
            });

            using var response = await harness.ReadResponseAsync(10);
            var changes = response.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("changes");

            Assert.Contains(changes.EnumerateObject(), property =>
                string.Equals(property.Name, project.XamlUri, StringComparison.Ordinal));
            Assert.DoesNotContain(changes.EnumerateObject(), property =>
                string.Equals(property.Name, project.CodeUri, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "axsg-mcp-tests-" + Guid.NewGuid().ToString("N"));
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

    private static async Task<CrossLanguageNavigationFixture> CreateCrossLanguageNavigationFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-mcp-cross-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "MainView.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        const string codeText = """
                                using System;

                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]
                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:TestApp", "TestApp")]

                                namespace Avalonia.Metadata
                                {
                                    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                    public sealed class XmlnsDefinitionAttribute : Attribute
                                    {
                                        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                    }
                                }

                                namespace TestApp.Controls
                                {
                                    public class UserControl { }

                                    public class TextBlock
                                    {
                                        public string Text { get; set; } = string.Empty;
                                    }
                                }

                                namespace TestApp
                                {
                                    public partial class MainView : TestApp.Controls.UserControl { }

                                    public sealed class MainViewModel
                                    {
                                        public string Name { get; set; } = string.Empty;

                                        public string GetName() => Name;
                                    }
                                }
                                """;

        const string xamlText = """
                                <UserControl xmlns="https://github.com/avaloniaui"
                                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                             xmlns:vm="using:TestApp"
                                             x:Class="TestApp.MainView"
                                             x:DataType="vm:MainViewModel">
                                  <TextBlock Text="{Binding Name}" />
                                  <TextBlock Text="{= GetName()}" />
                                </UserControl>
                                """;

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);

        return new CrossLanguageNavigationFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            GetPosition(codeText, codeText.IndexOf("MainViewModel", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainView", StringComparison.Ordinal) + 2),
            new Uri(xamlPath).AbsoluteUri);
    }

    private static Compilation CreateCompilationWithExternalControls()
    {
        const string metadataSource = """
                                      namespace ExtLib.Controls
                                      {
                                          public class ExternalButton
                                          {
                                              public string Content { get; set; } = string.Empty;
                                          }
                                      }
                                      """;

        var metadataSyntaxTree = CSharpSyntaxTree.ParseText(metadataSource, path: "/tmp/ExtLib.Controls.cs");
        var coreReferences = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        };
        var metadataCompilation = CSharpCompilation.Create(
            assemblyName: "ExtLib.Controls",
            syntaxTrees: [metadataSyntaxTree],
            references: coreReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var metadataStream = new MemoryStream();
        var emitResult = metadataCompilation.Emit(metadataStream);
        if (!emitResult.Success)
        {
            throw new InvalidOperationException("Failed to emit metadata compilation for external-controls MCP integration test.");
        }

        metadataStream.Position = 0;
        var metadataReference = MetadataReference.CreateFromImage(metadataStream.ToArray());

        const string hostSource = """
                                  using System;

                                  namespace Avalonia.Metadata
                                  {
                                      [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                      public sealed class XmlnsDefinitionAttribute : Attribute
                                      {
                                          public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                      }
                                  }

                                  [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Host.Controls")]

                                  namespace Host.Controls
                                  {
                                      public class UserControl { }
                                  }
                                  """;

        var hostSyntaxTree = CSharpSyntaxTree.ParseText(hostSource, path: "/tmp/Host.Controls.cs");
        return CSharpCompilation.Create(
            assemblyName: "Host.Controls",
            syntaxTrees: [hostSyntaxTree],
            references: [.. coreReferences, metadataReference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static async Task<RenameProjectFixture> CreateRenameProjectFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-mcp-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "TestApp.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        const string codeText = """
                                using System;

                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]
                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:TestApp", "TestApp")]

                                namespace Avalonia.Metadata
                                {
                                    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                    public sealed class XmlnsDefinitionAttribute : Attribute
                                    {
                                        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                    }
                                }

                                namespace TestApp.Controls
                                {
                                    public class UserControl { }

                                    public class TextBlock
                                    {
                                        public string Text { get; set; } = string.Empty;
                                    }
                                }

                                namespace TestApp
                                {
                                    public class MainViewModel
                                    {
                                        public string Name { get; set; } = string.Empty;

                                        public string GetName() => Name;
                                    }
                                }
                                """;

        const string xamlText = """
                                <UserControl xmlns="https://github.com/avaloniaui"
                                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                             xmlns:vm="using:TestApp"
                                             x:DataType="vm:MainViewModel">
                                  <TextBlock Text="{Binding Name}" />
                                  <TextBlock Text="{= GetName()}" />
                                </UserControl>
                                """;

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);

        return new RenameProjectFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            GetPosition(codeText, codeText.IndexOf("Name { get;", StringComparison.Ordinal) + 2),
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("Name", StringComparison.Ordinal) + 2));
    }

    private static SourcePosition GetPosition(string text, int offset)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < offset && index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return new SourcePosition(line, character);
    }

    private sealed class McpServerHarness : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly Stream _clientWriteStream;
        private readonly Stream _serverReadStream;
        private readonly Stream _serverWriteStream;
        private readonly Stream _clientReadStream;
        private readonly JsonRpcMessageReader _clientReader;
        private readonly AxsgMcpServer _server;
        private readonly CancellationTokenSource _cts;
        private readonly Task<int> _runTask;

        private McpServerHarness(string? workspaceRoot = null, ICompilationProvider? compilationProvider = null)
        {
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new JsonRpcMessageReader(_clientReadStream);

            var engine = compilationProvider is null
                ? new XamlLanguageServiceEngine()
                : new XamlLanguageServiceEngine(compilationProvider);
            _server = new AxsgMcpServer(
                new JsonRpcMessageReader(_serverReadStream),
                new JsonRpcMessageWriter(_serverWriteStream),
                engine,
                new XamlLanguageServiceOptions(workspaceRoot));
            _runTask = _server.RunAsync(_cts.Token);
        }

        public static Task<McpServerHarness> StartAsync(string? workspaceRoot = null, ICompilationProvider? compilationProvider = null)
        {
            return Task.FromResult(new McpServerHarness(workspaceRoot, compilationProvider));
        }

        public async Task InitializeAsync()
        {
            await SendRequestAsync(100, "initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "tests",
                    ["version"] = "1.0.0"
                }
            });
            using var _ = await ReadResponseAsync(100);
            await SendNotificationAsync("notifications/initialized", new JsonObject());
        }

        public Task SendRequestAsync(int id, string method, JsonObject parameters)
        {
            var payload = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            };

            return SendAsync(payload);
        }

        public Task SendNotificationAsync(string method, JsonObject parameters)
        {
            var payload = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };

            return SendAsync(payload);
        }

        public async Task<JsonDocument> ReadResponseAsync(int id)
        {
            while (true)
            {
                var document = await _clientReader.ReadMessageAsync(_cts.Token);
                if (document is null)
                {
                    throw new EndOfStreamException();
                }

                if (!document.RootElement.TryGetProperty("id", out var idElement) ||
                    idElement.GetInt32() != id)
                {
                    document.Dispose();
                    continue;
                }

                return document;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch
            {
                // Cancellation is expected during harness shutdown.
            }

            _server.Dispose();
            _clientWriteStream.Dispose();
            _serverReadStream.Dispose();
            _serverWriteStream.Dispose();
            _clientReadStream.Dispose();
            _cts.Dispose();
        }

        private Task SendAsync(JsonObject payload)
        {
            var writer = new JsonRpcMessageWriter(_clientWriteStream);
            return writer.WriteAsync(payload, _cts.Token);
        }
    }

    private sealed record RenameProjectFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        SourcePosition CodeNamePosition,
        string XamlUri,
        string XamlText,
        SourcePosition XamlNamePosition);

    private sealed record CrossLanguageNavigationFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        SourcePosition ViewModelTypePosition,
        SourcePosition MainViewTypePosition,
        string XamlUri) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
