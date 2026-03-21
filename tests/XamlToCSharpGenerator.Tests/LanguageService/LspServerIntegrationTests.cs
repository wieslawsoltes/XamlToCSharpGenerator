using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageServer.Protocol;
using XamlToCSharpGenerator.LanguageServer.Server;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class LspServerIntegrationTests
{
    private static readonly SemaphoreSlim SharedRepositoryHarnessGate = new(1, 1);
    private static readonly Lazy<Task<LspServerHarness>> SharedRepositoryHarness =
        new(CreateSharedRepositoryHarnessAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact]
    public async Task Initialize_ReturnsDeclaredCapabilities()
    {
        await using var harness = await LspServerHarness.StartAsync();

        await harness.SendRequestAsync(1, "initialize", new JsonObject
        {
            ["processId"] = null,
            ["rootUri"] = "file:///tmp",
            ["capabilities"] = new JsonObject()
        });

        using var response = await harness.ReadResponseAsync(1);
        var result = response.RootElement.GetProperty("result");
        var capabilities = result.GetProperty("capabilities");

        Assert.True(capabilities.GetProperty("hoverProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("definitionProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("declarationProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("referencesProvider").GetBoolean());
        var codeActionKinds = capabilities.GetProperty("codeActionProvider").GetProperty("codeActionKinds");
        Assert.True(codeActionKinds.GetArrayLength() > 0);
        Assert.Contains(codeActionKinds.EnumerateArray(), static item => item.GetString() == "quickfix");
        Assert.Contains(codeActionKinds.EnumerateArray(), static item => item.GetString() == "refactor.rewrite");
        Assert.True(capabilities.GetProperty("documentHighlightProvider").GetBoolean());
        Assert.False(capabilities.GetProperty("documentLinkProvider").GetProperty("resolveProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("workspaceSymbolProvider").GetBoolean());
        Assert.Equal(4, capabilities.GetProperty("signatureHelpProvider").GetProperty("triggerCharacters").GetArrayLength());
        Assert.True(capabilities.GetProperty("foldingRangeProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("selectionRangeProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("linkedEditingRangeProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("documentSymbolProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("documentFormattingProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("inlayHintProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("semanticTokensProvider").GetProperty("full").GetBoolean());
        Assert.Equal(2, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32());
    }

    [Fact]
    public async Task Formatting_Request_ReturnsFullDocumentEdit()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/FormattingView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><StackPanel><TextBlock Text=\"Hello\"/></StackPanel></UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(11, "textDocument/formatting", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["options"] = new JsonObject
            {
                ["tabSize"] = 2,
                ["insertSpaces"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(11);
        var edits = response.RootElement.GetProperty("result");
        Assert.Equal(JsonValueKind.Array, edits.ValueKind);
        Assert.Equal(1, edits.GetArrayLength());

        var edit = edits[0];
        Assert.Equal(0, edit.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(0, edit.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(0, edit.GetProperty("range").GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(xaml.Length, edit.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"Hello\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>",
            edit.GetProperty("newText").GetString());
    }

    [Fact]
    public async Task Formatting_Request_PreservesXmlDeclarationEncoding()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/FormattingWithDeclaration.axaml";
        const string xaml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<UserControl xmlns=\"https://github.com/avaloniaui\"><StackPanel><TextBlock Text=\"Hello\"/></StackPanel></UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(111, "textDocument/formatting", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["options"] = new JsonObject
            {
                ["tabSize"] = 2,
                ["insertSpaces"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(111);
        var edits = response.RootElement.GetProperty("result");
        var edit = edits[0];
        var newText = edit.GetProperty("newText").GetString();
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n", newText, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"Hello\" />", newText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FoldingRange_Request_ReturnsElementAndCommentRegions()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/FoldingView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <!--\n" +
            "    comment\n" +
            "  -->\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"Hello\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(12, "textDocument/foldingRange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            }
        });

        using var response = await harness.ReadResponseAsync(12);
        var ranges = response.RootElement.GetProperty("result");
        Assert.Equal(JsonValueKind.Array, ranges.ValueKind);

        Assert.Contains(ranges.EnumerateArray(), static range =>
            range.GetProperty("startLine").GetInt32() == 0 &&
            range.GetProperty("endLine").GetInt32() == 7 &&
            string.Equals(range.GetProperty("kind").GetString(), "region", StringComparison.Ordinal));

        Assert.Contains(ranges.EnumerateArray(), static range =>
            range.GetProperty("startLine").GetInt32() == 1 &&
            range.GetProperty("endLine").GetInt32() == 3 &&
            string.Equals(range.GetProperty("kind").GetString(), "comment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectionRange_Request_ReturnsRecursiveParentChain()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/SelectionView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"Hello\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Hello", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(13, "textDocument/selectionRange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["positions"] = new JsonArray
            {
                new JsonObject
                {
                    ["line"] = position.Line,
                    ["character"] = position.Character
                }
            }
        });

        using var response = await harness.ReadResponseAsync(13);
        var ranges = response.RootElement.GetProperty("result");
        Assert.Equal(1, ranges.GetArrayLength());

        var enumerator = ranges.EnumerateArray();
        Assert.True(enumerator.MoveNext());
        var selection = enumerator.Current;
        Assert.Equal("Hello", GetRangeText(xaml, selection.GetProperty("range")));

        var attributeSelection = selection.GetProperty("parent");
        Assert.Equal("Text=\"Hello\"", GetRangeText(xaml, attributeSelection.GetProperty("range")));

        var elementSelection = attributeSelection.GetProperty("parent");
        Assert.Equal("<TextBlock Text=\"Hello\" />", GetRangeText(xaml, elementSelection.GetProperty("range")));

        var stackPanelSelection = elementSelection.GetProperty("parent");
        Assert.Equal(
            "<StackPanel>\n    <TextBlock Text=\"Hello\" />\n  </StackPanel>",
            GetRangeText(xaml, stackPanelSelection.GetProperty("range")));
    }

    [Fact]
    public async Task LinkedEditingRange_Request_ReturnsStartAndEndTagNameRanges()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/LinkedEditingView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock>Hello</TextBlock>\n" +
            "  </StackPanel>\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("TextBlock", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(14, "textDocument/linkedEditingRange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(14);
        var result = response.RootElement.GetProperty("result");
        var ranges = result.GetProperty("ranges");
        Assert.Equal(2, ranges.GetArrayLength());

        var enumerator = ranges.EnumerateArray();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("TextBlock", GetRangeText(xaml, enumerator.Current));
        Assert.True(enumerator.MoveNext());
        Assert.Equal("TextBlock", GetRangeText(xaml, enumerator.Current));
        Assert.Equal(@"[-.\w:]+", result.GetProperty("wordPattern").GetString());
    }

    [Fact]
    public async Task LinkedEditingRange_Request_UsesMatchingClosingTagForNestedElements()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/NestedLinkedEditingView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Grid>\n" +
            "    <TextBlock>Hello</TextBlock>\n" +
            "  </Grid>\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Grid", StringComparison.Ordinal) + 1);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(114, "textDocument/linkedEditingRange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(114);
        var result = response.RootElement.GetProperty("result");
        var ranges = result.GetProperty("ranges");
        Assert.Equal(2, ranges.GetArrayLength());

        var enumerator = ranges.EnumerateArray();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("Grid", GetRangeText(xaml, enumerator.Current));
        Assert.True(enumerator.MoveNext());
        Assert.Equal("Grid", GetRangeText(xaml, enumerator.Current));
    }

    [Fact]
    public async Task DocumentHighlight_Request_ReturnsDeclarationAndUsage()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DocumentHighlights.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <UserControl.Resources>\n" +
            "    <SolidColorBrush x:Key=\"AccentBrush\" />\n" +
            "  </UserControl.Resources>\n" +
            "  <Border Background=\"{StaticResource AccentBrush}\" />\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(15, "textDocument/documentHighlight", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(15);
        var highlights = response.RootElement.GetProperty("result");
        Assert.Equal(2, highlights.GetArrayLength());

        Assert.Contains(highlights.EnumerateArray(), highlight =>
            highlight.GetProperty("kind").GetInt32() == (int)XamlDocumentHighlightKind.Write &&
            string.Equals(GetRangeText(xaml, highlight.GetProperty("range")), "AccentBrush", StringComparison.Ordinal));
        Assert.Contains(highlights.EnumerateArray(), highlight =>
            highlight.GetProperty("kind").GetInt32() == (int)XamlDocumentHighlightKind.Read &&
            string.Equals(GetRangeText(xaml, highlight.GetProperty("range")), "AccentBrush", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definition_Request_ForStaticResource_ReturnsResourceKeyRange()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ResourceDefinitionView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <UserControl.Resources>\n" +
            "    <SolidColorBrush x:Key=\"AccentBrush\" Color=\"Red\" />\n" +
            "  </UserControl.Resources>\n" +
            "  <Border Background=\"{StaticResource AccentBrush}\" />\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(16, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(16);
        var definitions = response.RootElement.GetProperty("result");
        Assert.Equal(1, definitions.GetArrayLength());
        Assert.Equal(
            "AccentBrush",
            GetRangeText(xaml, definitions[0].GetProperty("range")));
    }

    [Fact]
    public async Task DocumentLink_Request_ReturnsResolvedIncludeTarget()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "axsg-doc-links-lsp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var includedFilePath = Path.Combine(temporaryDirectory, "SharedStyles.axaml");
            await File.WriteAllTextAsync(
                includedFilePath,
                "<Styles xmlns=\"https://github.com/avaloniaui\" />",
                CancellationToken.None);
            var includedUri = new Uri(includedFilePath).AbsoluteUri;

            var hostFilePath = Path.Combine(temporaryDirectory, "Host.axaml");
            var hostUri = new Uri(hostFilePath).AbsoluteUri;
            var xaml =
                "<Styles xmlns=\"https://github.com/avaloniaui\">\n" +
                $"  <StyleInclude Source=\"{includedUri}\" />\n" +
                "</Styles>";

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = hostUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = xaml
                }
            });

            await harness.SendRequestAsync(17, "textDocument/documentLink", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = hostUri
                }
            });

            using var response = await harness.ReadResponseAsync(17);
            var links = response.RootElement.GetProperty("result");
            Assert.Equal(1, links.GetArrayLength());
            Assert.Equal(includedUri, links[0].GetProperty("target").GetString());
            Assert.Equal(includedUri, GetRangeText(xaml, links[0].GetProperty("range")));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task WorkspaceSymbol_Request_ReturnsMatchingOpenDocumentSymbols()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string firstUri = "file:///tmp/WorkspaceSymbolOne.axaml";
        const string secondUri = "file:///tmp/WorkspaceSymbolTwo.axaml";
        const string firstXaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Grid x:Name=\"LayoutRoot\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />\n" +
            "</UserControl>";
        const string secondXaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel />\n" +
            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = firstUri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = firstXaml
            }
        });

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = secondUri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = secondXaml
            }
        });

        await harness.SendRequestAsync(18, "workspace/symbol", new JsonObject
        {
            ["query"] = "LayoutRoot"
        });

        using var response = await harness.ReadResponseAsync(18);
        var symbols = response.RootElement.GetProperty("result");
        Assert.Equal(1, symbols.GetArrayLength());
        Assert.Equal(firstUri, symbols[0].GetProperty("location").GetProperty("uri").GetString());
        Assert.Contains("LayoutRoot", symbols[0].GetProperty("name").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceSymbol_Request_RefreshesDiscovery_After_Watched_File_Changes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-lsp-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var projectPath = Path.Combine(tempRoot, "TestApp.csproj");
        var rootViewPath = Path.Combine(tempRoot, "MainView.axaml");
        var addedViewPath = Path.Combine(tempRoot, "FreshView.axaml");
        const string freshSymbolName = "FreshNode";

        await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(
            rootViewPath,
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

        try
        {
            await using var harness = await LspServerHarness.StartAsync(workspaceRoot: tempRoot);
            await harness.InitializeAsync();

            await harness.SendRequestAsync(181, "workspace/symbol", new JsonObject
            {
                ["query"] = freshSymbolName
            });

            using (var initialResponse = await harness.ReadResponseAsync(181))
            {
                Assert.Equal(0, initialResponse.RootElement.GetProperty("result").GetArrayLength());
            }

            await File.WriteAllTextAsync(
                addedViewPath,
                """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Grid x:Name="FreshNode" />
                </UserControl>
                """);

            await harness.SendNotificationAsync("workspace/didChangeWatchedFiles", new JsonObject
            {
                ["changes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["uri"] = new Uri(addedViewPath).AbsoluteUri,
                        ["type"] = 1
                    }
                }
            });

            await harness.SendRequestAsync(182, "workspace/symbol", new JsonObject
            {
                ["query"] = freshSymbolName
            });

            using var updatedResponse = await harness.ReadResponseAsync(182);
            JsonElement symbols = updatedResponse.RootElement.GetProperty("result");
            Assert.Equal(1, symbols.GetArrayLength());
            Assert.Equal(new Uri(addedViewPath).AbsoluteUri, symbols[0].GetProperty("location").GetProperty("uri").GetString());
            Assert.Contains(freshSymbolName, symbols[0].GetProperty("name").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WatchedFileChanges_RefreshDiagnostics_ForOpenXamlDocuments()
    {
        const string xamlUri = "file:///tmp/MainView.axaml";
        const string projectUri = "file:///tmp/TestApp.csproj";
        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.MainView\" />\n";

        var provider = new TogglingCompilationProvider(
            CreateAdHocCompilation(
                """
                using System;

                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]

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
                }

                namespace TestApp
                {
                    public class MainView : TestApp.Controls.UserControl { }
                }
                """,
                assemblyName: "WatchedFileInitial",
                filePath: "/tmp/WatchedFileInitial.cs"),
            CreateAdHocCompilation(
                """
                using System;

                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]

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
                }

                namespace TestApp
                {
                    public partial class MainView : TestApp.Controls.UserControl { }
                }
                """,
                assemblyName: "WatchedFileUpdated",
                filePath: "/tmp/WatchedFileUpdated.cs"));

        await using var harness = await LspServerHarness.StartAsync(provider);
        await harness.InitializeAsync();
        await harness.OpenDocumentAsync(xamlUri, xamlText);

        using var initialPublish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        JsonElement initialParams = initialPublish.RootElement.GetProperty("params");
        Assert.Equal(xamlUri, initialParams.GetProperty("uri").GetString());
        JsonElement initialDiagnostics = initialParams.GetProperty("diagnostics");
        Assert.Contains(
            initialDiagnostics.EnumerateArray(),
            diagnostic => string.Equals(diagnostic.GetProperty("code").GetString(), "AXSG0109", StringComparison.Ordinal));

        await harness.SendNotificationAsync("workspace/didChangeWatchedFiles", new JsonObject
        {
            ["changes"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = projectUri,
                    ["type"] = 1
                }
            }
        });

        using var updatedPublish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        JsonElement updatedParams = updatedPublish.RootElement.GetProperty("params");
        Assert.Equal(xamlUri, updatedParams.GetProperty("uri").GetString());
        Assert.Equal(0, updatedParams.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(1, provider.InvalidateCalls);
    }

    [Fact]
    public async Task WatchedFileChanges_InvalidateFailure_DoesNotCrashServer()
    {
        var provider = new ThrowingInvalidateCompilationProvider(
            CreateAdHocCompilation(
                "namespace TestApp { public sealed class Placeholder { } }",
                assemblyName: "WatchedFileException",
                filePath: "/tmp/WatchedFileException.cs"));

        await using var harness = await LspServerHarness.StartAsync(provider);
        await harness.InitializeAsync();

        await harness.SendNotificationAsync("workspace/didChangeWatchedFiles", new JsonObject
        {
            ["changes"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = "file:///tmp/.tmp-tests/build-integration/missing/BuildIntegration.csproj",
                    ["type"] = 3
                }
            }
        });

        using var logMessage = await harness.ReadNotificationAsync("window/logMessage");
        var loggedMessage = logMessage.RootElement
            .GetProperty("params")
            .GetProperty("message")
            .GetString();
        Assert.Contains("workspace/didChangeWatchedFiles", loggedMessage, StringComparison.Ordinal);
        Assert.Contains("Could not find a part of the path", loggedMessage, StringComparison.Ordinal);

        await harness.SendRequestAsync(901, "workspace/symbol", new JsonObject
        {
            ["query"] = "Placeholder"
        });

        using var response = await harness.ReadResponseAsync(901);
        Assert.True(response.RootElement.TryGetProperty("result", out var result));
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
    }

    [Fact]
    public async Task SignatureHelp_Request_ReturnsBindingSignature()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/SignatureHelpView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <TextBlock Text=\"{Binding Name, Mode=TwoWay}\" />\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Mode", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(19, "textDocument/signatureHelp", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(19);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(0, result.GetProperty("activeSignature").GetInt32());
        Assert.Equal(1, result.GetProperty("activeParameter").GetInt32());
        var signatures = result.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(
            "Binding(path, Mode, Source, RelativeSource, ElementName, Converter, ConverterParameter, StringFormat, FallbackValue, TargetNullValue)",
            signatures[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task SignatureHelp_Request_ReturnsXBindSignature()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/XBindSignatureHelpView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <TextBlock Text=\"{x:Bind Name, Mode=TwoWay}\" />\n" +
            "</UserControl>";
        var position = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Mode", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(1901, "textDocument/signatureHelp", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(1901);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(0, result.GetProperty("activeSignature").GetInt32());
        Assert.Equal(1, result.GetProperty("activeParameter").GetInt32());
        var signatures = result.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(
            "x:Bind(path, Mode, BindBack, ElementName, RelativeSource, Source, DataType, Converter, ConverterCulture, ConverterLanguage, ConverterParameter, StringFormat, FallbackValue, TargetNullValue, Delay, Priority, UpdateSourceTrigger)",
            signatures[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task DidOpen_InvalidXaml_PublishesDiagnostics()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/BrokenView.axaml";
        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = "<UserControl>\n  <Button>\n</UserControl>"
            }
        });

        using var publish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        var parameters = publish.RootElement.GetProperty("params");
        Assert.Equal(uri, parameters.GetProperty("uri").GetString());
        Assert.True(parameters.GetProperty("diagnostics").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Completion_Request_ReturnsItems()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/CompletionView.axaml";
        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = "<Us"
            }
        });

        await harness.SendRequestAsync(2, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = 3
            }
        });

        using var response = await harness.ReadResponseAsync(2);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        var hasUserControl = false;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("label", out var label))
            {
                continue;
            }

            if (label.GetString()?.EndsWith("UserControl", StringComparison.Ordinal) == true)
            {
                hasUserControl = true;
                break;
            }
        }

        Assert.True(hasUserControl);
    }

    [Fact]
    public async Task Completion_Request_UsesSnippetInsertTextFormat_ForPropertyTemplate()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/CompletionSnippetView.axaml";
        const string xaml = "<Path xmlns=\"https://github.com/avaloniaui\" ";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(201, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.Length
            }
        });

        using var response = await harness.ReadResponseAsync(201);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("label", out var label) ||
                !string.Equals(label.GetString(), "Data", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Equal("Data=\"$0\"", item.GetProperty("insertText").GetString());
            Assert.Equal(2, item.GetProperty("insertTextFormat").GetInt32());
            return;
        }

        Assert.Fail("Expected completion item for Path.Data was not returned.");
    }

    [Fact]
    public async Task Completion_Request_ForQualifiedPropertyElement_ReturnsPropertyItems()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/QualifiedPropertyElementCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.O\n" +
                            "  </Path>\n" +
                            "</UserControl>";
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("<Path.O", StringComparison.Ordinal) + "<Path.O".Length);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2011, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2011);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item =>
            string.Equals(item.GetProperty("label").GetString(), "Opacity", StringComparison.Ordinal) &&
            string.Equals(item.GetProperty("textEdit").GetProperty("newText").GetString(), "Path.Opacity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForBindingPath_ReturnsBindingMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/BindingCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding }\"/>\n" +
                            "</UserControl>";
        var bindingCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{Binding }", StringComparison.Ordinal) + "{Binding ".Length);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(202, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = bindingCaret.Line,
                ["character"] = bindingCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(202);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "FirstName", StringComparison.Ordinal));
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "GetCustomer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForXBindDataTemplatePath_ReturnsTemplateRootAndNamedMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/XBindTemplateCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" " +
                            "x:Class=\"TestApp.Controls.MainView\">\n" +
                            "  <TextBlock x:Name=\"Editor\" Text=\"ready\"/>\n" +
                            "  <DataTemplate x:DataType=\"vm:CustomerViewModel\">\n" +
                            "    <TextBlock Text=\"{x:Bind }\"/>\n" +
                            "  </DataTemplate>\n" +
                            "</UserControl>";
        var bindingCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{x:Bind }", StringComparison.Ordinal) + "{x:Bind ".Length);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2202, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = bindingCaret.Line,
                ["character"] = bindingCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2202);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "DisplayName", StringComparison.Ordinal));
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "RootOnly", StringComparison.Ordinal));
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "Editor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForMarkupExtension_UsesTextEditToReplaceTypedOpeningBrace()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/MarkupExtensionCompletionView.axaml";
        const string xaml = "<Window xmlns=\"https://github.com/avaloniaui\" Title=\"{\" />";
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{", StringComparison.Ordinal) + 1);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2019, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2019);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item =>
            string.Equals(item.GetProperty("label").GetString(), "Binding", StringComparison.Ordinal) &&
            string.Equals(item.GetProperty("textEdit").GetProperty("newText").GetString(), "{Binding $0}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForBindingPath_OnNonStringTarget_ReturnsBindingMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/BindingCompletionWidthView.axaml";
        const string xaml = "<Window xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\" Width=\"{Binding }\">\n" +
                            "</Window>";
        var bindingCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{Binding }", StringComparison.Ordinal) + "{Binding ".Length);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2021, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = bindingCaret.Line,
                ["character"] = bindingCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2021);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "FirstName", StringComparison.Ordinal));
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "GetCustomer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForExpressionBinding_ReturnsExpressionMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ExpressionCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= Customer.Dis}\"/>\n" +
                            "</UserControl>";
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Dis", StringComparison.Ordinal) + 3);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(203, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = expressionCaret.Line,
                ["character"] = expressionCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(203);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "DisplayName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForImplicitExpression_ReturnsMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ImplicitExpressionCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Prod}\"/>\n" +
                            "</UserControl>";
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Prod", StringComparison.Ordinal) + 4);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2032, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2032);
        var items = response.RootElement.GetProperty("result").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "ProductName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForRootShorthand_ReturnsRootMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/RootShorthandCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:Class=\"TestApp.Controls.MainView\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{this.}\"/>\n" +
                            "</UserControl>";
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{this.}", StringComparison.Ordinal) + "{this.".Length);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2033, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2033);
        var items = response.RootElement.GetProperty("result").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "RootOnly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForExplicitInlineCSharp_ReturnsMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineCSharpCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CSharp Code=source.}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("source.", StringComparison.Ordinal) + "source.".Length);
        await harness.SendRequestAsync(230, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(230);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "ProductName", StringComparison.Ordinal));
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "FormatSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForExplicitInlineCSharpCData_ReturnsMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineCSharpCDataCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock>\n" +
                            "    <TextBlock.Text>\n" +
                            "      <CSharp><![CDATA[\n" +
                            "source.\n" +
                            "      ]]></CSharp>\n" +
                            "    </TextBlock.Text>\n" +
                            "  </TextBlock>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("source.", StringComparison.Ordinal) + "source.".Length);
        await harness.SendRequestAsync(2301, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2301);
        var items = response.RootElement
            .GetProperty("result")
            .GetProperty("items");

        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "ProductName", StringComparison.Ordinal));
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "FormatSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Request_ForExplicitInlineCSharpLambdaParameterMemberAccess_ReturnsMembers()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineCSharpLambdaCompletionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" " +
                            "xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button AdvancedClick=\"{axsg:CSharp Code=(senderArg, argsArg) => argsArg.}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("argsArg.", StringComparison.Ordinal) + "argsArg.".Length);
        await harness.SendRequestAsync(2304, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2304);
        var items = response.RootElement.GetProperty("result").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "Handled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definition_Request_ForEventLambdaProperty_ReturnsClrSourceLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/EventLambdaDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button Click=\"{(s, e) => ClickCount++}\"/>\n" +
                            "</UserControl>";
        var offset = xaml.IndexOf("ClickCount", StringComparison.Ordinal);
        var position = SourceText.From(xaml).Lines.GetLinePosition(offset + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(2033, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = position.Line,
                ["character"] = position.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2033);
        var result = response.RootElement.GetProperty("result");
        var first = result.ValueKind == JsonValueKind.Array ? result[0] : result;
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, first.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completion_Request_For_Empty_ExpressionBinding_In_SourceGenCatalogSample_ReturnsMembers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "ExpressionBindingsPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var originalText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string existingExpression = "<TextBlock Text=\"{= FirstName + ' ' + LastName}\" />";
        const string emptyExpression = "<TextBlock Text=\"{= }\" />";
        Assert.Contains(existingExpression, originalText, StringComparison.Ordinal);
        var xaml = originalText.Replace(existingExpression, emptyExpression, StringComparison.Ordinal);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf(emptyExpression, StringComparison.Ordinal) + "<TextBlock Text=\"{= ".Length);
        var uri = new Uri(xamlPath).AbsoluteUri;

        await SharedRepositoryHarnessGate.WaitAsync();
        try
        {
            var harness = await SharedRepositoryHarness.Value;
            await harness.OpenDocumentAsync(uri, xaml);

            await harness.SendRequestAsync(2031, "textDocument/completion", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = caret.Line,
                    ["character"] = caret.Character
                }
            });

            using var response = await harness.ReadResponseAsync(2031);
            var items = response.RootElement
                .GetProperty("result")
                .GetProperty("items");

            Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "FirstName", StringComparison.Ordinal));
            Assert.Contains(items.EnumerateArray(), item => string.Equals(item.GetProperty("label").GetString(), "FormatSummary", StringComparison.Ordinal));
            await harness.CloseDocumentAsync(uri);
        }
        finally
        {
            SharedRepositoryHarnessGate.Release();
        }
    }

    [Fact]
    public async Task Definition_Request_For_InlineCSharpCData_In_SourceGenCatalogSample_ReturnsClrDeclarations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodeCDataPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xaml = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var methodOffset = xaml.IndexOf("RecordSender", StringComparison.Ordinal);
        var propertyOffset = xaml.IndexOf("ClickCount = 0", StringComparison.Ordinal);
        Assert.True(methodOffset >= 0, "Expected inline CDATA method usage not found.");
        Assert.True(propertyOffset >= 0, "Expected inline CDATA property usage not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        await SharedRepositoryHarnessGate.WaitAsync();
        try
        {
            var harness = await SharedRepositoryHarness.Value;
            await harness.OpenDocumentAsync(uri, xaml);

            var methodCaret = SourceText.From(xaml).Lines.GetLinePosition(methodOffset + 2);
            await harness.SendRequestAsync(2034, "textDocument/definition", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = methodCaret.Line,
                    ["character"] = methodCaret.Character
                }
            });

            using var methodResponse = await harness.ReadResponseAsync(2034);
            var methodResult = methodResponse.RootElement.GetProperty("result");
            var methodFirst = methodResult.ValueKind == JsonValueKind.Array ? methodResult[0] : methodResult;
            Assert.Contains("InlineCodePageViewModel.cs", methodFirst.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);

            var propertyCaret = SourceText.From(xaml).Lines.GetLinePosition(propertyOffset + 2);
            await harness.SendRequestAsync(2035, "textDocument/definition", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = propertyCaret.Line,
                    ["character"] = propertyCaret.Character
                }
            });

            using var propertyResponse = await harness.ReadResponseAsync(2035);
            var propertyResult = propertyResponse.RootElement.GetProperty("result");
            var propertyFirst = propertyResult.ValueKind == JsonValueKind.Array ? propertyResult[0] : propertyResult;
            Assert.Contains("InlineCodePageViewModel.cs", propertyFirst.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
            await harness.CloseDocumentAsync(uri);
        }
        finally
        {
            SharedRepositoryHarnessGate.Release();
        }
    }

    [Fact]
    public async Task Definition_Request_For_InlineCSharpCompactMarkup_In_SourceGenCatalogSample_ReturnsClrDeclarations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodePage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xaml = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var propertyOffset = xaml.IndexOf("{CSharp Code=source.ProductName}", StringComparison.Ordinal);
        var methodOffset = xaml.IndexOf("source.RecordSender(sender)", StringComparison.Ordinal);
        Assert.True(propertyOffset >= 0, "Expected inline compact property usage not found.");
        Assert.True(methodOffset >= 0, "Expected inline compact method usage not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        await SharedRepositoryHarnessGate.WaitAsync();
        try
        {
            var harness = await SharedRepositoryHarness.Value;
            await harness.OpenDocumentAsync(uri, xaml);

            var propertyCaret = SourceText.From(xaml).Lines.GetLinePosition(propertyOffset + "{CSharp Code=source.".Length + 2);
            await harness.SendRequestAsync(2036, "textDocument/definition", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = propertyCaret.Line,
                    ["character"] = propertyCaret.Character
                }
            });

            using var propertyResponse = await harness.ReadResponseAsync(2036);
            var propertyResult = propertyResponse.RootElement.GetProperty("result");
            var propertyFirst = propertyResult.ValueKind == JsonValueKind.Array ? propertyResult[0] : propertyResult;
            Assert.Contains("InlineCodePageViewModel.cs", propertyFirst.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);

            var methodCaret = SourceText.From(xaml).Lines.GetLinePosition(methodOffset + "source.".Length + 2);
            await harness.SendRequestAsync(2037, "textDocument/definition", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = methodCaret.Line,
                    ["character"] = methodCaret.Character
                }
            });

            using var methodResponse = await harness.ReadResponseAsync(2037);
            var methodResult = methodResponse.RootElement.GetProperty("result");
            var methodFirst = methodResult.ValueKind == JsonValueKind.Array ? methodResult[0] : methodResult;
            Assert.Contains("InlineCodePageViewModel.cs", methodFirst.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
            await harness.CloseDocumentAsync(uri);
        }
        finally
        {
            SharedRepositoryHarnessGate.Release();
        }
    }

    [Fact]
    public async Task Definition_Request_For_InlineCSharpContextVariable_In_SourceGenCatalogSample_ReturnsClrTypeDeclaration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodeCDataPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xaml = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var sourceOffset = xaml.IndexOf("source.ClickCount++;", StringComparison.Ordinal);
        Assert.True(sourceOffset >= 0, "Expected inline CDATA source usage not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        await SharedRepositoryHarnessGate.WaitAsync();
        try
        {
            var harness = await SharedRepositoryHarness.Value;
            await harness.OpenDocumentAsync(uri, xaml);

            var sourceCaret = SourceText.From(xaml).Lines.GetLinePosition(sourceOffset + 2);
            await harness.SendRequestAsync(2038, "textDocument/definition", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = sourceCaret.Line,
                    ["character"] = sourceCaret.Character
                }
            });

            using var response = await harness.ReadResponseAsync(2038);
            var result = response.RootElement.GetProperty("result");
            var first = result.ValueKind == JsonValueKind.Array ? result[0] : result;

            Assert.Contains("InlineCodePageViewModel.cs", first.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
            await harness.CloseDocumentAsync(uri);
        }
        finally
        {
            SharedRepositoryHarnessGate.Release();
        }
    }

    [Fact]
    public async Task Hover_Request_ReturnsElementDetails()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/HoverView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button Content=\"Save\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(20, "textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = 3
            }
        });

        using var response = await harness.ReadResponseAsync(20);
        var result = response.RootElement.GetProperty("result");
        var contents = result.GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("Element", contents, StringComparison.Ordinal);
        Assert.Contains("Button", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_Request_ForBindingProperty_ReturnsPropertyDetails()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/HoverBindingProperty.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Customer.DisplayName}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var propertyOffset = xaml.IndexOf("DisplayName", StringComparison.Ordinal);
        var propertyCaret = SourceText.From(xaml).Lines.GetLinePosition(propertyOffset + 2);

        await harness.SendRequestAsync(21, "textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = propertyCaret.Line,
                ["character"] = propertyCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(21);
        var contents = response.RootElement.GetProperty("result").GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("Property", contents, StringComparison.Ordinal);
        Assert.Contains("CustomerViewModel.DisplayName", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_Request_ForExpressionMethod_ReturnsMethodDetails()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/HoverExpressionMethod.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FormatSummary(FirstName, LastName, Count)}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var methodOffset = xaml.IndexOf("FormatSummary", StringComparison.Ordinal);
        var methodCaret = SourceText.From(xaml).Lines.GetLinePosition(methodOffset + 2);

        await harness.SendRequestAsync(22, "textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = methodCaret.Line,
                ["character"] = methodCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(22);
        var contents = response.RootElement.GetProperty("result").GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("Method", contents, StringComparison.Ordinal);
        Assert.Contains("FormatSummary", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Definition_Request_ForExplicitInlineCSharpProperty_ReturnsPropertyDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineCSharpDefinition.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" " +
                            "xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{axsg:CSharp Code=source.ProductName}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var propertyOffset = xaml.IndexOf("ProductName", StringComparison.Ordinal);
        var propertyCaret = SourceText.From(xaml).Lines.GetLinePosition(propertyOffset + 2);
        await harness.SendRequestAsync(231, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = propertyCaret.Line,
                ["character"] = propertyCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(231);
        var result = response.RootElement.GetProperty("result");
        var first = result.ValueKind == JsonValueKind.Array ? result[0] : result;
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, first.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_Request_ForInlineCSharpCDataProperty_And_Method_ReturnClrDeclarations()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineCSharpCDataDefinition.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button>\n" +
                            "    <Button.Click>\n" +
                            "      <CSharp><![CDATA[\n" +
                            "source.RecordSender(sender);\n" +
                            "source.ClickCount = 0;\n" +
                            "      ]]></CSharp>\n" +
                            "    </Button.Click>\n" +
                            "  </Button>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var methodOffset = xaml.IndexOf("RecordSender", StringComparison.Ordinal);
        var methodCaret = SourceText.From(xaml).Lines.GetLinePosition(methodOffset + 2);
        await harness.SendRequestAsync(2311, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = methodCaret.Line,
                ["character"] = methodCaret.Character
            }
        });

        using var methodResponse = await harness.ReadResponseAsync(2311);
        var methodResult = methodResponse.RootElement.GetProperty("result");
        var methodFirst = methodResult.ValueKind == JsonValueKind.Array ? methodResult[0] : methodResult;
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, methodFirst.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);

        var propertyOffset = xaml.IndexOf("ClickCount", StringComparison.Ordinal);
        var propertyCaret = SourceText.From(xaml).Lines.GetLinePosition(propertyOffset + 2);
        await harness.SendRequestAsync(2312, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = propertyCaret.Line,
                ["character"] = propertyCaret.Character
            }
        });

        using var propertyResponse = await harness.ReadResponseAsync(2312);
        var propertyResult = propertyResponse.RootElement.GetProperty("result");
        var propertyFirst = propertyResult.ValueKind == JsonValueKind.Array ? propertyResult[0] : propertyResult;
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, propertyFirst.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_Request_ForInlineCSharpLocalVariable_ReturnsLocalXamlDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlineCSharpLocalDefinition.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" " +
                            "xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button Click=\"{axsg:CSharp Code=(s, e) => { var clickCount = source.ClickCount; source.ClickCount = clickCount; }}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var usageOffset = xaml.LastIndexOf("clickCount", StringComparison.Ordinal);
        var declarationOffset = xaml.IndexOf("clickCount", StringComparison.Ordinal);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(usageOffset + 2);
        await harness.SendRequestAsync(2314, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = caret.Line,
                ["character"] = caret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(2314);
        var result = response.RootElement.GetProperty("result");
        var first = result.ValueKind == JsonValueKind.Array ? result[0] : result;
        Assert.Equal(uri, first.GetProperty("uri").GetString());
        Assert.Equal(SourceText.From(xaml).Lines.GetLinePosition(declarationOffset).Line, first.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task Hover_Request_ForXDataTypeValue_ReturnsTypeDetails()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/HoverXDataType.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var typeOffset = xaml.IndexOf("MainWindowViewModel", StringComparison.Ordinal);
        var typeCaret = SourceText.From(xaml).Lines.GetLinePosition(typeOffset + 2);

        await harness.SendRequestAsync(23, "textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = typeCaret.Line,
                ["character"] = typeCaret.Character
            }
        });

        using var response = await harness.ReadResponseAsync(23);
        var contents = response.RootElement.GetProperty("result").GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("Data Type", contents, StringComparison.Ordinal);
        Assert.Contains("TestApp.Controls.MainWindowViewModel", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_Request_ForXamlBindingProperty_ReturnsWorkspaceEdit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = project.XamlText
                }
            });

            await harness.SendRequestAsync(211, "textDocument/rename", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = project.XamlNamePosition.Line,
                    ["character"] = project.XamlNamePosition.Character
                },
                ["newName"] = "DisplayName"
            });

            using var response = await harness.ReadResponseAsync(211);
            var changes = response.RootElement.GetProperty("result").GetProperty("changes");

            Assert.True(changes.TryGetProperty(project.XamlUri, out var xamlEdits));
            Assert.True(changes.TryGetProperty(project.CodeUri, out var codeEdits));
            Assert.True(xamlEdits.GetArrayLength() > 0);
            Assert.True(codeEdits.GetArrayLength() > 0);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeAction_Request_ForXamlBindingProperty_ReturnsRenameAction()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = project.XamlText
                }
            });

            await harness.SendRequestAsync(213, "textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri
                },
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject
                    {
                        ["line"] = project.XamlNamePosition.Line,
                        ["character"] = project.XamlNamePosition.Character
                    },
                    ["end"] = new JsonObject
                    {
                        ["line"] = project.XamlNamePosition.Line,
                        ["character"] = project.XamlNamePosition.Character
                    }
                },
                ["context"] = new JsonObject
                {
                    ["diagnostics"] = new JsonArray()
                }
            });

            using var response = await harness.ReadResponseAsync(213);
            var actions = response.RootElement.GetProperty("result");
            Assert.True(actions.GetArrayLength() > 0);

            var command = actions[0].GetProperty("command");
            Assert.Equal("axsg.refactor.renameSymbol", command.GetProperty("command").GetString());
            Assert.Equal("refactor.rename", actions[0].GetProperty("kind").GetString());
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeAction_Request_ForBindingMarkup_ReturnsRewriteEdit()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/BindingConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                            "             xmlns:vm=\"using:TestApp.Controls\"\n" +
                            "             x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\" />\n" +
                            "</UserControl>";
        var bindingCharacter = xaml.IndexOf("Binding", StringComparison.Ordinal) + 2;
        var bindingPosition = GetPosition(xaml, bindingCharacter);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(214, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = bindingPosition.Line,
                    ["character"] = bindingPosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = bindingPosition.Line,
                    ["character"] = bindingPosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(214);
        var actions = response.RootElement.GetProperty("result");
        JsonElement rewriteAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Convert to CompiledBinding")
            {
                continue;
            }

            rewriteAction = action;
            found = true;
            break;
        }

        Assert.True(
            found,
            "Expected invalid compiled binding quick fix. Titles: " +
            string.Join(", ", actions.EnumerateArray().Select(static item => item.GetProperty("title").GetString())));
        Assert.Equal("refactor.rewrite", rewriteAction.GetProperty("kind").GetString());
        var edits = rewriteAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Equal("CompiledBinding", edits[0].GetProperty("newText").GetString());
    }

    [Fact]
    public async Task CodeAction_Request_ForPropertyAttribute_ReturnsPropertyElementRewrite()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/PropertyElementConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <TextBlock Text=\"Hello\" />\n" +
                            "</UserControl>";
        var attributePosition = GetPosition(xaml, xaml.IndexOf("Text=", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(216, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = attributePosition.Line,
                    ["character"] = attributePosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = attributePosition.Line,
                    ["character"] = attributePosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(216);
        var actions = response.RootElement.GetProperty("result");
        JsonElement rewriteAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Convert attribute to property element")
            {
                continue;
            }

            rewriteAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("refactor.rewrite", rewriteAction.GetProperty("kind").GetString());
        var edits = rewriteAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains("<TextBlock.Text>Hello</TextBlock.Text>", edits[0].GetProperty("newText").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeAction_Request_ForCompiledBindingWithoutDataType_ReturnsQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/CompiledBindingWithoutDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding Name}\" />\n" +
                            "</UserControl>";
        var bindingPosition = GetPosition(xaml, xaml.IndexOf("CompiledBinding", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(217, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = bindingPosition.Line,
                    ["character"] = bindingPosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = bindingPosition.Line,
                    ["character"] = bindingPosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(217);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Fix missing x:DataType by converting to Binding")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Equal("Binding", edits[0].GetProperty("newText").GetString());
    }

    [Fact]
    public async Task CodeAction_Request_ForCompiledBindingWithInvalidPath_ReturnsQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/CompiledBindingInvalidPathView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                            "             xmlns:vm=\"using:TestApp.Controls\"\n" +
                            "             x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding MissingName}\" />\n" +
                            "</UserControl>";
        var bindingPosition = GetPosition(xaml, xaml.IndexOf("CompiledBinding", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(217, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = bindingPosition.Line,
                    ["character"] = bindingPosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = bindingPosition.Line,
                    ["character"] = bindingPosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(217);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Fix invalid compiled binding path by converting to Binding")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Equal("Binding", edits[0].GetProperty("newText").GetString());
    }

    [Fact]
    public async Task CodeAction_Request_ForXClassWithoutPartial_ReturnsQuickFix()
    {
        var project = await CreateXClassPartialProjectFixtureAsync(isPartial: false);
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = project.XamlText
                }
            });

            await harness.SendRequestAsync(218, "textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri
                },
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject
                    {
                        ["line"] = project.XamlClassPosition.Line,
                        ["character"] = project.XamlClassPosition.Character
                    },
                    ["end"] = new JsonObject
                    {
                        ["line"] = project.XamlClassPosition.Line,
                        ["character"] = project.XamlClassPosition.Character
                    }
                },
                ["context"] = new JsonObject
                {
                    ["diagnostics"] = new JsonArray()
                }
            });

            using var response = await harness.ReadResponseAsync(218);
            var actions = response.RootElement.GetProperty("result");
            JsonElement quickFixAction = default;
            var found = false;
            foreach (var action in actions.EnumerateArray())
            {
                if (action.GetProperty("title").GetString() != "AXSG: Fix x:Class companion type by adding partial")
                {
                    continue;
                }

                quickFixAction = action;
                found = true;
                break;
            }

            Assert.True(found);
            Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
            Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());

            var changes = quickFixAction.GetProperty("edit").GetProperty("changes");
            Assert.True(changes.TryGetProperty(project.CodeUri, out var edits));
            Assert.Equal("partial ", edits[0].GetProperty("newText").GetString());
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeAction_Request_ForInvalidXClassModifier_ReturnsQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InvalidXClassModifier.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.MainView\" x:ClassModifier=\"friend\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(219, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = 0,
                    ["character"] = 0
                },
                ["end"] = new JsonObject
                {
                    ["line"] = 0,
                    ["character"] = 0
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(219);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Set x:ClassModifier to public")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "public", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_Request_ForMismatchedXClassModifier_ReturnsQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/MismatchedXClassModifier.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.MainView\" x:ClassModifier=\"internal\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(220, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = 0,
                    ["character"] = 0
                },
                ["end"] = new JsonObject
                {
                    ["line"] = 0,
                    ["character"] = 0
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(220);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Set x:ClassModifier to public")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "public", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_Request_ForMissingEventHandler_ReturnsQuickFix()
    {
        var project = await CreateEventHandlerProjectFixtureAsync(withIncompatibleHandler: false);
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = project.XamlText
                }
            });

            await harness.SendRequestAsync(2201, "textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri
                },
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject
                    {
                        ["line"] = project.EventHandlerPosition.Line,
                        ["character"] = project.EventHandlerPosition.Character
                    },
                    ["end"] = new JsonObject
                    {
                        ["line"] = project.EventHandlerPosition.Line,
                        ["character"] = project.EventHandlerPosition.Character
                    }
                },
                ["context"] = new JsonObject
                {
                    ["diagnostics"] = new JsonArray()
                }
            });

            using var response = await harness.ReadResponseAsync(2201);
            var actions = response.RootElement.GetProperty("result");
            JsonElement quickFixAction = default;
            var found = false;
            foreach (var action in actions.EnumerateArray())
            {
                if (action.GetProperty("title").GetString() != "AXSG: Add event handler 'OnClick'")
                {
                    continue;
                }

                quickFixAction = action;
                found = true;
                break;
            }

            Assert.True(found);
            Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
            Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
            var changes = quickFixAction.GetProperty("edit").GetProperty("changes");
            Assert.True(changes.TryGetProperty(project.CodeUri, out var edits));
            Assert.Contains(edits.EnumerateArray(), edit =>
                edit.GetProperty("newText").GetString()!.Contains("private void OnClick(", StringComparison.Ordinal) &&
                edit.GetProperty("newText").GetString()!.Contains("global::System.EventArgs e", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeAction_Request_ForMissingIncludeTarget_ReturnsQuickFix()
    {
        var project = await CreateMissingIncludeProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = project.XamlText
                }
            });

            await harness.SendRequestAsync(2202, "textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri
                },
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject
                    {
                        ["line"] = project.IncludePosition.Line,
                        ["character"] = project.IncludePosition.Character
                    },
                    ["end"] = new JsonObject
                    {
                        ["line"] = project.IncludePosition.Line,
                        ["character"] = project.IncludePosition.Character
                    }
                },
                ["context"] = new JsonObject
                {
                    ["diagnostics"] = new JsonArray()
                }
            });

            using var response = await harness.ReadResponseAsync(2202);
            var actions = response.RootElement.GetProperty("result");
            JsonElement quickFixAction = default;
            var found = false;
            foreach (var action in actions.EnumerateArray())
            {
                if (action.GetProperty("title").GetString() != "AXSG: Add included XAML file to project")
                {
                    continue;
                }

                quickFixAction = action;
                found = true;
                break;
            }

            Assert.True(found);
            Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
            Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
            var changes = quickFixAction.GetProperty("edit").GetProperty("changes");
            Assert.True(changes.TryGetProperty(project.ProjectUri, out var edits));
            Assert.Contains(edits.EnumerateArray(), edit =>
                edit.GetProperty("newText").GetString()!.Contains("<AvaloniaXaml Include=\"Themes/Shared.axaml\" />", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeAction_Request_ForInvalidInclude_ReturnsQuickFix()
    {
        var project = await CreateInvalidIncludeProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync(
                "textDocument/didOpen",
                new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = project.XamlUri,
                        ["languageId"] = "axaml",
                        ["version"] = 1,
                        ["text"] = project.XamlText
                    }
                });

            await harness.SendRequestAsync(2203, "textDocument/codeAction",
                new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = project.XamlUri
                    },
                    ["range"] = new JsonObject
                    {
                        ["start"] = new JsonObject
                        {
                            ["line"] = project.IncludePosition.Line,
                            ["character"] = project.IncludePosition.Character
                        },
                        ["end"] = new JsonObject
                        {
                            ["line"] = project.IncludePosition.Line,
                            ["character"] = project.IncludePosition.Character
                        }
                    },
                    ["context"] = new JsonObject
                    {
                        ["diagnostics"] = new JsonArray()
                    }
                });

            using var response = await harness.ReadResponseAsync(2203);
            var actions = response.RootElement.GetProperty("result");
            JsonElement quickFixAction = default;
            var found = false;
            foreach (var action in actions.EnumerateArray())
            {
                if (action.GetProperty("title").GetString() != "AXSG: Remove invalid include")
                {
                    continue;
                }

                quickFixAction = action;
                found = true;
                break;
            }

            Assert.True(found);
            Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
            Assert.True(quickFixAction.TryGetProperty("edit", out _));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeAction_Request_ForUnresolvedElementType_ReturnsNamespaceImportQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/NamespaceImportView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\">\n" +
                            "  <ThemeDoodad />\n" +
                            "</UserControl>";
        var elementPosition = GetPosition(xaml, xaml.IndexOf("ThemeDoodad", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(219, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = elementPosition.Line,
                    ["character"] = elementPosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = elementPosition.Line,
                    ["character"] = elementPosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(219);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Import namespace for local:ThemeDoodad")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "local:ThemeDoodad", StringComparison.Ordinal));
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString()?.Trim(), "xmlns:local=\"using:Demo.Controls\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_Request_ForUnresolvedAttachedPropertyOwner_ReturnsNamespaceImportQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/NamespaceImportAttachedPropertyView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\">\n" +
                            "  <UserControl ThemeDoodad.Accent=\"True\" />\n" +
                            "</UserControl>";
        var propertyPosition = GetPosition(xaml, xaml.IndexOf("ThemeDoodad.Accent", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(220, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = propertyPosition.Line,
                    ["character"] = propertyPosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = propertyPosition.Line,
                    ["character"] = propertyPosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(220);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Import namespace for local:ThemeDoodad")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "local:ThemeDoodad.Accent", StringComparison.Ordinal));
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), " xmlns:local=\"using:Demo.Controls\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_Request_ForUnresolvedSetterPropertyOwner_ReturnsNamespaceImportQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/NamespaceImportSetterPropertyView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"UserControl\">\n" +
                            "      <Setter Property=\"ThemeDoodad.Accent\" Value=\"True\" />\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";
        var propertyPosition = GetPosition(xaml, xaml.IndexOf("ThemeDoodad.Accent", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(221, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = propertyPosition.Line,
                    ["character"] = propertyPosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = propertyPosition.Line,
                    ["character"] = propertyPosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(221);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Import namespace for local:ThemeDoodad")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "local:ThemeDoodad.Accent", StringComparison.Ordinal));
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), " xmlns:local=\"using:Demo.Controls\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_Request_ForUnresolvedXDataTypeValue_ReturnsNamespaceImportQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/NamespaceImportXDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:DataType=\"ThemeDoodad\" />";
        var typePosition = GetPosition(xaml, xaml.IndexOf("ThemeDoodad", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(222, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = typePosition.Line,
                    ["character"] = typePosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = typePosition.Line,
                    ["character"] = typePosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(222);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Import namespace for local:ThemeDoodad")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "local:ThemeDoodad", StringComparison.Ordinal));
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString()?.Trim(), "xmlns:local=\"using:Demo.Controls\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_Request_ForUnresolvedControlThemeTargetType_ReturnsNamespaceImportQuickFix()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/NamespaceImportControlThemeTargetType.axaml";
        const string xaml = "<ControlTheme xmlns=\"using:Host.Controls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ThemeDoodad}\" />";
        var typePosition = GetPosition(xaml, xaml.IndexOf("ThemeDoodad", StringComparison.Ordinal) + 2);

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(223, "textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = typePosition.Line,
                    ["character"] = typePosition.Character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = typePosition.Line,
                    ["character"] = typePosition.Character
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        });

        using var response = await harness.ReadResponseAsync(223);
        var actions = response.RootElement.GetProperty("result");
        JsonElement quickFixAction = default;
        var found = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("title").GetString() != "AXSG: Import namespace for local:ThemeDoodad")
            {
                continue;
            }

            quickFixAction = action;
            found = true;
            break;
        }

        Assert.True(found);
        Assert.Equal("quickfix", quickFixAction.GetProperty("kind").GetString());
        Assert.True(quickFixAction.GetProperty("isPreferred").GetBoolean());
        var edits = quickFixAction.GetProperty("edit").GetProperty("changes").GetProperty(uri);
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString(), "{x:Type local:ThemeDoodad}", StringComparison.Ordinal));
        Assert.Contains(edits.EnumerateArray(), edit =>
            string.Equals(edit.GetProperty("newText").GetString()?.Trim(), "xmlns:local=\"using:Demo.Controls\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AxsgRename_Request_ForCSharpProperty_ReturnsWorkspaceEdit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider(), project.RootPath);
            await harness.InitializeAsync();

            await harness.SendRequestAsync(212, "axsg/refactor/rename", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.CodeUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = project.CodeNamePosition.Line,
                    ["character"] = project.CodeNamePosition.Character
                },
                ["documentText"] = project.CodeText,
                ["newName"] = "DisplayName"
            });

            using var response = await harness.ReadResponseAsync(212);
            var changes = response.RootElement.GetProperty("result").GetProperty("changes");

            Assert.True(changes.TryGetProperty(project.XamlUri, out _));
            Assert.True(changes.TryGetProperty(project.CodeUri, out _));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CSharpRenamePropagation_Request_ForProperty_ReturnsXamlOnlyWorkspaceEdit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(215, "axsg/csharp/renamePropagation", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.CodeUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = project.CodeNamePosition.Line,
                    ["character"] = project.CodeNamePosition.Character
                },
                ["documentText"] = project.CodeText,
                ["newName"] = "DisplayName"
            });

            using var response = await harness.ReadResponseAsync(215);
            var changes = response.RootElement.GetProperty("result").GetProperty("changes");

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

    [Fact]
    public async Task CSharpRenamePropagation_Request_ForMethod_ReturnsExpressionBindingEdits()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(216, "axsg/csharp/renamePropagation", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.CodeUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = project.CodeMethodPosition.Line,
                    ["character"] = project.CodeMethodPosition.Character
                },
                ["documentText"] = project.CodeText,
                ["newName"] = "BuildName"
            });

            using var response = await harness.ReadResponseAsync(216);
            var changes = response.RootElement.GetProperty("result").GetProperty("changes");

            var xamlChanges = changes.EnumerateObject()
                .FirstOrDefault(property => string.Equals(property.Name, project.XamlUri, StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(xamlChanges.Name));
            Assert.DoesNotContain(changes.EnumerateObject(), property =>
                string.Equals(property.Name, project.CodeUri, StringComparison.Ordinal));
            Assert.Contains("BuildName", xamlChanges.Value.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CSharpRenamePropagation_Request_ForType_ReturnsXDataTypeEdits()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(217, "axsg/csharp/renamePropagation", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.CodeUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = project.CodeTypePosition.Line,
                    ["character"] = project.CodeTypePosition.Character
                },
                ["documentText"] = project.CodeText,
                ["newName"] = "ShellViewModel"
            });

            using var response = await harness.ReadResponseAsync(217);
            var changes = response.RootElement.GetProperty("result").GetProperty("changes");

            var xamlChanges = changes.EnumerateObject()
                .FirstOrDefault(property => string.Equals(property.Name, project.XamlUri, StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(xamlChanges.Name));
            Assert.DoesNotContain(changes.EnumerateObject(), property =>
                string.Equals(property.Name, project.CodeUri, StringComparison.Ordinal));
            Assert.Contains("ShellViewModel", xamlChanges.Value.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task AxsgRename_Request_ForXamlBindingProperty_ReturnsWorkspaceEdit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
            await harness.InitializeAsync();

            await harness.SendRequestAsync(214, "axsg/refactor/rename", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = project.XamlUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = project.XamlNamePosition.Line,
                    ["character"] = project.XamlNamePosition.Character
                },
                ["documentText"] = project.XamlText,
                ["newName"] = "DisplayName"
            });

            using var response = await harness.ReadResponseAsync(214);
            var changes = response.RootElement.GetProperty("result").GetProperty("changes");

            Assert.True(changes.TryGetProperty(project.XamlUri, out _));
            Assert.True(changes.TryGetProperty(project.CodeUri, out _));
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task InlayHint_Request_ReturnsCompiledBindingTypeHint()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/InlayHintView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding Customer}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(21, "textDocument/inlayHint", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = 0,
                    ["character"] = 0
                },
                ["end"] = new JsonObject
                {
                    ["line"] = 2,
                    ["character"] = 14
                }
            }
        });

        using var response = await harness.ReadResponseAsync(21);
        var hints = response.RootElement.GetProperty("result");
        Assert.Equal(1, hints.GetArrayLength());

        var hint = hints[0];
        var labelParts = hint.GetProperty("label");
        Assert.Equal(JsonValueKind.Array, labelParts.ValueKind);
        Assert.Equal(": ", labelParts[0].GetProperty("value").GetString());
        Assert.Equal("CustomerViewModel", labelParts[1].GetProperty("value").GetString());
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, labelParts[1].GetProperty("location").GetProperty("uri").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, hint.GetProperty("kind").GetInt32());
        Assert.Contains("Compiled Binding", hint.GetProperty("tooltip").GetProperty("value").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InlayHint_Request_ForElementNameBinding_ReturnsTypeHint()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ElementNameInlayHintView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(22, "textDocument/inlayHint", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = 0,
                    ["character"] = 0
                },
                ["end"] = new JsonObject
                {
                    ["line"] = 3,
                    ["character"] = 14
                }
            }
        });

        using var response = await harness.ReadResponseAsync(22);
        var hints = response.RootElement.GetProperty("result");
        Assert.Equal(1, hints.GetArrayLength());

        var hint = hints[0];
        Assert.Contains("TestApp.Controls.Button", hint.GetProperty("tooltip").GetProperty("value").GetString(), StringComparison.Ordinal);
        Assert.Equal("string", hint.GetProperty("label")[1].GetProperty("value").GetString());
    }

    [Fact]
    public async Task References_Request_ReturnsDeclarationAndUsageLocations()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(3, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = 45
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(3);
        var references = response.RootElement.GetProperty("result");
        Assert.True(references.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Definition_Request_ForXDataTypeAttributeValue_ReturnsTypeLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionXDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:Button\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(31, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.IndexOf("vm:Button", StringComparison.Ordinal) + 5
            }
        });

        using var response = await harness.ReadResponseAsync(31);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForXClassAttributeValue_ReturnsTypeLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionXClassView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.Button\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(32, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.IndexOf("TestApp.Controls.Button", StringComparison.Ordinal) + 8
            }
        });

        using var response = await harness.ReadResponseAsync(32);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForStyleIncludeSource_Returns_Target_Xaml_File()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-lsp-include-def-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "project");
        var themesDir = Path.Combine(projectDir, "Themes");
        Directory.CreateDirectory(themesDir);

        var projectPath = Path.Combine(projectDir, "TestApp.csproj");
        var openFilePath = Path.Combine(projectDir, "MainView.axaml");
        var targetFilePath = Path.Combine(themesDir, "Fluent.xaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        const string xaml = """
<UserControl xmlns="https://github.com/avaloniaui">
  <UserControl.Styles>
    <StyleInclude Source="/Themes/Fluent.xaml" />
  </UserControl.Styles>
</UserControl>
""";

        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="MainView.axaml" />
                <AvaloniaXaml Include="Themes/Fluent.xaml" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(openFilePath, xaml);
        await File.WriteAllTextAsync(targetFilePath, "<Styles xmlns=\"https://github.com/avaloniaui\" />");

        try
        {
            await using var harness = await LspServerHarness.StartAsync(workspaceRoot: projectDir);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = openUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = xaml
                }
            });

            await harness.SendRequestAsync(23121, "textDocument/definition", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = openUri
                },
                ["position"] = new JsonObject
                {
                    ["line"] = 2,
                    ["character"] = xaml.Split('\n')[2].IndexOf("Fluent.xaml", StringComparison.Ordinal) + 2
                }
            });

            using var response = await harness.ReadResponseAsync(23121);
            var definitions = response.RootElement.GetProperty("result");
            Assert.Equal(1, definitions.GetArrayLength());
            Assert.Equal(new Uri(targetFilePath).AbsoluteUri, definitions[0].GetProperty("uri").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Definition_Request_ForBindingPathProperty_ReturnsPropertyLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionBindingPathView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(320, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = xaml.IndexOf("{Binding Name}", StringComparison.Ordinal) - xaml.LastIndexOf('\n', xaml.IndexOf("{Binding Name}", StringComparison.Ordinal)) + "{Binding ".Length
            }
        });

        using var response = await harness.ReadResponseAsync(320);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForQualifiedPropertyElement_ReturnsPropertyLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionQualifiedPropertyElementView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var opacityOffset = xaml.IndexOf("Opacity", StringComparison.Ordinal);
        Assert.True(opacityOffset >= 0, "Expected qualified property element token not found.");
        var lineOffset = opacityOffset - xaml.LastIndexOf('\n', opacityOffset);

        await harness.SendRequestAsync(3201, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = lineOffset + 2
            }
        });

        using var response = await harness.ReadResponseAsync(3201);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForExpressionBindingProperty_ReturnsPropertyLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionExpressionBindingView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FirstName + ' - ' + LastName}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var firstNameOffset = xaml.IndexOf("FirstName", StringComparison.Ordinal);
        Assert.True(firstNameOffset >= 0, "Expected expression property token not found.");
        var lineOffset = firstNameOffset - xaml.LastIndexOf('\n', firstNameOffset);

        await harness.SendRequestAsync(3205, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = lineOffset + 1
            }
        });

        using var response = await harness.ReadResponseAsync(3205);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForRootShorthand_ReturnsPropertyLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionRootShorthandView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:Class=\"TestApp.Controls.MainView\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{this.Title}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var titleOffset = xaml.IndexOf("this.Title", StringComparison.Ordinal);
        Assert.True(titleOffset >= 0, "Expected root shorthand token not found.");
        var lineOffset = titleOffset - xaml.LastIndexOf('\n', titleOffset);

        await harness.SendRequestAsync(3206, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = lineOffset + "this.".Length + 1
            }
        });

        using var response = await harness.ReadResponseAsync(3206);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForDynamicResourceValue_ReturnsResourceDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionDynamicResourceView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <SolidColorBrush x:Key=\"AccentButtonBackgroundDisabled\" Color=\"Red\"/>\n" +
                            "  </UserControl.Resources>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button\">\n" +
                            "      <Setter Property=\"Background\" Value=\"{DynamicResource AccentButtonBackgroundDisabled}\"/>\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var dynamicResourceOffset = xaml.IndexOf("AccentButtonBackgroundDisabled", xaml.IndexOf("DynamicResource", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(dynamicResourceOffset >= 0, "Expected DynamicResource key token not found.");
        var lineOffset = dynamicResourceOffset - xaml.LastIndexOf('\n', dynamicResourceOffset);

        await harness.SendRequestAsync(321, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 6,
                ["character"] = lineOffset + 1
            }
        });

        using var response = await harness.ReadResponseAsync(321);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
        Assert.Equal(uri, definitions[0].GetProperty("uri").GetString());
        Assert.Equal(2, definitions[0].GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task Definition_Request_ForStyleClassValue_ReturnsSelectorDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionStyleClassView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock.warning\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <TextBlock Classes=\"warning\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var classOffset = xaml.IndexOf("warning", xaml.IndexOf("Classes=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(classOffset >= 0, "Expected style class token not found.");
        var lineOffset = classOffset - xaml.LastIndexOf('\n', classOffset);

        await harness.SendRequestAsync(322, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 4,
                ["character"] = lineOffset + 1
            }
        });

        using var response = await harness.ReadResponseAsync(322);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
        Assert.Equal(uri, definitions[0].GetProperty("uri").GetString());
        Assert.Equal(2, definitions[0].GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task Definition_Request_ForComplexSelectorMiddleType_ReturnsTypeDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionComplexSelectorMiddleTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Border.local-card > StackPanel > TextBlock.subtitle\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <Border Classes=\"local-card\">\n" +
                            "    <StackPanel>\n" +
                            "      <TextBlock Classes=\"subtitle\"/>\n" +
                            "    </StackPanel>\n" +
                            "  </Border>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var stackPanelOffset = xaml.IndexOf("StackPanel", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(stackPanelOffset >= 0, "Expected StackPanel selector token not found.");
        var lineOffset = stackPanelOffset - xaml.LastIndexOf('\n', stackPanelOffset);

        await harness.SendRequestAsync(3225, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = lineOffset + 1
            }
        });

        using var response = await harness.ReadResponseAsync(3225);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_Request_ForSelectorPseudoClass_ReturnsPseudoClassDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionSelectorPseudoClassView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button:pressed\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var pseudoOffset = xaml.IndexOf("pressed", StringComparison.Ordinal);
        Assert.True(pseudoOffset >= 0, "Expected pseudoclass token not found.");
        var lineOffset = pseudoOffset - xaml.LastIndexOf('\n', pseudoOffset);

        await harness.SendRequestAsync(323, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = lineOffset + 1
            }
        });

        using var response = await harness.ReadResponseAsync(323);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Definition_Request_ForSelectorNamedElement_WithPseudoClass_ReturnsNamedElementDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionSelectorNamedElementView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <ToggleButton x:Name=\"ThemeToggle\"/>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"ToggleButton#ThemeToggle:checked\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var nameOffset = xaml.IndexOf("ThemeToggle", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(nameOffset >= 0, "Expected selector named element token not found.");
        var lineOffset = nameOffset - xaml.LastIndexOf('\n', nameOffset);

        await harness.SendRequestAsync(3235, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 3,
                ["character"] = lineOffset + 1
            }
        });

        using var response = await harness.ReadResponseAsync(3235);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
        Assert.Equal(uri, definitions[0].GetProperty("uri").GetString());
        Assert.Equal(1, definitions[0].GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task Definition_Request_ForQualifiedElementPrefix_ReturnsXmlnsDeclaration()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DefinitionQualifiedElementPrefixView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:pages=\"clr-namespace:TestApp.Controls\">\n" +
                            "  <pages:Button />\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var prefixOffset = xaml.IndexOf("pages:Button", StringComparison.Ordinal);
        Assert.True(prefixOffset >= 0, "Expected qualified element token not found.");
        var lineOffset = prefixOffset - xaml.LastIndexOf('\n', prefixOffset);

        await harness.SendRequestAsync(32355, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = lineOffset + 2
            }
        });

        using var response = await harness.ReadResponseAsync(32355);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
        Assert.Equal(uri, definitions[0].GetProperty("uri").GetString());
        Assert.Equal(0, definitions[0].GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task References_Request_FromNamedElementDeclaration_Includes_SelectorUsage_WithPseudoClass()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesSelectorNamedElementDeclarationView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <ToggleButton x:Name=\"ThemeToggle\"/>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"ToggleButton#ThemeToggle:checked\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var declarationOffset = xaml.IndexOf("ThemeToggle", xaml.IndexOf("x:Name=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(declarationOffset >= 0, "Expected x:Name declaration token not found.");
        var lineOffset = declarationOffset - xaml.LastIndexOf('\n', declarationOffset);

        await harness.SendRequestAsync(3236, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = lineOffset + 1
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(3236);
        Assert.True(
            response.RootElement.TryGetProperty("result", out var references),
            "Expected result payload for references request. Actual: " + response.RootElement.GetRawText());
        Assert.True(references.GetArrayLength() >= 2);

        var sawDeclaration = false;
        var sawSelectorUsage = false;
        foreach (var reference in references.EnumerateArray())
        {
            if (reference.GetProperty("uri").GetString() != uri)
            {
                continue;
            }

            var line = reference.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32();
            if (line == 1)
            {
                sawDeclaration = true;
            }
            else if (line == 3)
            {
                sawSelectorUsage = true;
            }
        }

        Assert.True(sawDeclaration);
        Assert.True(sawSelectorUsage);
    }

    [Fact]
    public async Task Declaration_Request_ForXClassAttributeValue_ReturnsTypeLocation()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/DeclarationXClassView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.Button\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(35, "textDocument/declaration", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.IndexOf("TestApp.Controls.Button", StringComparison.Ordinal) + 8
            }
        });

        using var response = await harness.ReadResponseAsync(35);
        var definitions = response.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task MetadataDocument_Request_ReturnsFullMetadataFallbackDocument()
    {
        await using var harness = await LspServerHarness.StartAsync(
            new InMemoryCompilationProvider(CreateCompilationWithExternalControls()));
        await harness.InitializeAsync();

        const string uri = "file:///tmp/MetadataFallbackView.axaml";
        const string xaml = "<ext:ExternalButton xmlns:ext=\"using:ExtLib.Controls\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(360, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.IndexOf("ExternalButton", StringComparison.Ordinal) + 2
            }
        });

        using var definitionResponse = await harness.ReadResponseAsync(360);
        var definitions = definitionResponse.RootElement.GetProperty("result");
        Assert.True(definitions.GetArrayLength() >= 1);

        var metadataUri = definitions[0].GetProperty("uri").GetString();
        Assert.StartsWith("axsg-metadata:///", metadataUri, StringComparison.Ordinal);
        var metadataDocumentId = GetQueryParameter(metadataUri!, "id");
        Assert.False(string.IsNullOrWhiteSpace(metadataDocumentId));

        await harness.SendRequestAsync("metadata-1", "axsg/metadataDocument", new JsonObject
        {
            ["id"] = metadataDocumentId
        });

        using var metadataResponse = await harness.ReadResponseAsync("metadata-1");
        var documentText = metadataResponse.RootElement.GetProperty("result").GetProperty("text").GetString();
        Assert.NotNull(documentText);
        Assert.Contains("public class ExternalButton", documentText, StringComparison.Ordinal);
        Assert.Contains("public string Content", documentText, StringComparison.Ordinal);
        Assert.True(documentText!.Split('\n').Length > 8);
    }

    [Fact]
    public async Task PreviewProjectContext_Request_Returns_Project_And_TargetPath_Metadata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-lsp-preview-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var viewsDir = Path.Combine(tempRoot, "Views");
            Directory.CreateDirectory(viewsDir);

            var projectPath = Path.Combine(tempRoot, "PreviewApp.csproj");
            var xamlPath = Path.Combine(viewsDir, "MainView.axaml");
            var xamlUri = new Uri(xamlPath).AbsoluteUri;
            const string xamlText = "<UserControl xmlns=\"https://github.com/avaloniaui\" />";

            await File.WriteAllTextAsync(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <AvaloniaXaml Include="Views/MainView.axaml" Link="Linked/MainView.axaml" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(xamlPath, xamlText);

            await using var harness = await LspServerHarness.StartAsync(workspaceRoot: tempRoot);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = xamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = xamlText
                }
            });

            await harness.SendRequestAsync("preview-context-1", "axsg/preview/projectContext", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = xamlUri
                }
            });

            using var response = await harness.ReadResponseAsync("preview-context-1");
            var result = response.RootElement.GetProperty("result");
            Assert.Equal(projectPath, result.GetProperty("projectPath").GetString());
            Assert.Equal(tempRoot, result.GetProperty("projectDirectory").GetString());
            Assert.Equal(xamlPath, result.GetProperty("filePath").GetString());
            Assert.Equal("Linked/MainView.axaml", result.GetProperty("targetPath").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewProjectContext_Request_Prefers_Ancestor_Project_Over_Workspace_Root_Project()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-lsp-preview-context-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var rootProjectPath = Path.Combine(tempRoot, "PreviewHost.csproj");
            await File.WriteAllTextAsync(rootProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var libraryRoot = Path.Combine(tempRoot, "src", "PreviewLibrary");
            var viewsDir = Path.Combine(libraryRoot, "Views");
            Directory.CreateDirectory(viewsDir);

            var libraryProjectPath = Path.Combine(libraryRoot, "PreviewLibrary.csproj");
            var xamlPath = Path.Combine(viewsDir, "NestedView.axaml");
            var xamlUri = new Uri(xamlPath).AbsoluteUri;
            const string xamlText = "<UserControl xmlns=\"https://github.com/avaloniaui\" />";

            await File.WriteAllTextAsync(libraryProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <AvaloniaXaml Include="Views/NestedView.axaml" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(xamlPath, xamlText);

            await using var harness = await LspServerHarness.StartAsync(workspaceRoot: tempRoot);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = xamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = xamlText
                }
            });

            await harness.SendRequestAsync("preview-context-nested-1", "axsg/preview/projectContext", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = xamlUri
                }
            });

            using var response = await harness.ReadResponseAsync("preview-context-nested-1");
            var result = response.RootElement.GetProperty("result");
            Assert.Equal(libraryProjectPath, result.GetProperty("projectPath").GetString());
            Assert.Equal(libraryRoot, result.GetProperty("projectDirectory").GetString());
            Assert.Equal(xamlPath, result.GetProperty("filePath").GetString());
            Assert.Equal("Views/NestedView.axaml", result.GetProperty("targetPath").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewProjectContext_Request_Uses_Request_Workspace_Root_For_Linked_Shared_Xaml()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-lsp-preview-context-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var wrongWorkspaceRoot = Path.Combine(tempRoot, "workspace-a");
            Directory.CreateDirectory(wrongWorkspaceRoot);
            await File.WriteAllTextAsync(
                Path.Combine(wrongWorkspaceRoot, "WorkspaceA.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var unrelatedProjectRoot = Path.Combine(tempRoot, "src", "Aardvark");
            var libraryRoot = Path.Combine(tempRoot, "src", "PreviewLibrary");
            var sharedRoot = Path.Combine(tempRoot, "shared");
            Directory.CreateDirectory(unrelatedProjectRoot);
            Directory.CreateDirectory(libraryRoot);
            Directory.CreateDirectory(sharedRoot);

            await File.WriteAllTextAsync(
                Path.Combine(unrelatedProjectRoot, "Aardvark.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var libraryProjectPath = Path.Combine(libraryRoot, "PreviewLibrary.csproj");
            var xamlPath = Path.Combine(sharedRoot, "SharedView.axaml");
            var xamlUri = new Uri(xamlPath).AbsoluteUri;
            const string xamlText = "<UserControl xmlns=\"https://github.com/avaloniaui\" />";

            await File.WriteAllTextAsync(
                libraryProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <AvaloniaXaml Include="../../shared/SharedView.axaml" Link="Views/SharedView.axaml" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(xamlPath, xamlText);

            await using var harness = await LspServerHarness.StartAsync(workspaceRoot: wrongWorkspaceRoot);
            await harness.InitializeAsync();

            await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = xamlUri,
                    ["languageId"] = "axaml",
                    ["version"] = 1,
                    ["text"] = xamlText
                }
            });

            await harness.SendRequestAsync("preview-context-shared-1", "axsg/preview/projectContext", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = xamlUri
                },
                ["workspaceRoot"] = tempRoot
            });

            using var response = await harness.ReadResponseAsync("preview-context-shared-1");
            var result = response.RootElement.GetProperty("result");
            Assert.Equal(libraryProjectPath, result.GetProperty("projectPath").GetString());
            Assert.Equal(libraryRoot, result.GetProperty("projectDirectory").GetString());
            Assert.Equal(xamlPath, result.GetProperty("filePath").GetString());
            Assert.Equal("Views/SharedView.axaml", result.GetProperty("targetPath").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_Request_ForXDataTypeAttributeValue_ReturnsDeclarationAndUsage()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesXDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:Button\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(33, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.IndexOf("vm:Button", StringComparison.Ordinal) + 5
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(33);
        Assert.True(
            response.RootElement.TryGetProperty("result", out var references),
            "Expected result payload for references request. Actual: " + response.RootElement.GetRawText());
        Assert.True(references.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task References_Request_ForXClassAttributeValue_ReturnsDeclarationAndUsage()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesXClassView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.Button\" />";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(34, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 0,
                ["character"] = xaml.IndexOf("TestApp.Controls.Button", StringComparison.Ordinal) + 8
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(34);
        Assert.True(
            response.RootElement.TryGetProperty("result", out var references),
            "Expected result payload for references request. Actual: " + response.RootElement.GetRawText());
        Assert.True(references.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task References_Request_ForBindingPathProperty_ReturnsDeclarationAndUsage()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesBindingPathView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\"/>\n" +
                            "  <TextBlock Text=\"{Binding Path=Name}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(321, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = xaml.IndexOf("{Binding Name}", StringComparison.Ordinal) - xaml.LastIndexOf('\n', xaml.IndexOf("{Binding Name}", StringComparison.Ordinal)) + "{Binding ".Length
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(321);
        var references = response.RootElement.GetProperty("result");
        Assert.True(references.GetArrayLength() >= 3);
    }

    [Fact]
    public async Task References_Request_ForExpressionBindingProperty_ReturnsDeclarationAndUsage()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesExpressionBindingView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FirstName + ' - ' + LastName}\"/>\n" +
                            "  <TextBlock Text=\"{= FirstName + '!'}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        var firstNameOffset = xaml.IndexOf("FirstName", StringComparison.Ordinal);
        Assert.True(firstNameOffset >= 0, "Expected expression property token not found.");
        var lineOffset = firstNameOffset - xaml.LastIndexOf('\n', firstNameOffset);

        await harness.SendRequestAsync(3215, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = lineOffset + 1
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(3215);
        var references = response.RootElement.GetProperty("result");
        Assert.True(references.GetArrayLength() >= 3);
    }

    [Fact]
    public async Task References_Request_ForBindingProperty_IncludesExpressionUsage()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesBindingAndExpressionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding FirstName}\"/>\n" +
                            "  <TextBlock Text=\"{= FirstName + '!'}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(3216, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = xaml.IndexOf("{Binding FirstName}", StringComparison.Ordinal) - xaml.LastIndexOf('\n', xaml.IndexOf("{Binding FirstName}", StringComparison.Ordinal)) + "{Binding ".Length + 1
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(3216);
        var references = response.RootElement.GetProperty("result");
        Assert.True(references.GetArrayLength() >= 3);
    }

    [Fact]
    public async Task References_Request_WithStringId_DoesNotCrashAndPreservesId()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesStringIdView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        const string requestId = "internal-refs-1";
        await harness.SendRequestAsync(requestId, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = 45
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        using var response = await harness.ReadResponseAsync(requestId);
        Assert.Equal(requestId, response.RootElement.GetProperty("id").GetString());
        Assert.True(response.RootElement.GetProperty("result").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task UnknownMethod_Request_WithObjectId_DoesNotCrash()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRawRequestAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = new JsonObject
            {
                ["k"] = "i-token"
            },
            ["method"] = "axsg/unknownMethod",
            ["params"] = new JsonObject()
        });

        using var response = await harness.ReadErrorResponseWithObjectIdAsync("k", "i-token");
        var errorCode = response.RootElement.GetProperty("error").GetProperty("code").GetInt32();
        Assert.Equal(-32601, errorCode);
    }

    [Fact]
    public async Task CancelRequest_Notification_DoesNotBlockSubsequentRequests()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/CancelRequestView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(90, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = 45
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = true
            }
        });

        await harness.SendNotificationAsync("$/cancelRequest", new JsonObject
        {
            ["id"] = 90
        });

        await harness.SendRequestAsync(91, "textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 1,
                ["character"] = 3
            }
        });

        using var response = await harness.ReadResponseAsync(91);
        Assert.True(response.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task DidChange_IncrementalRangeUpdate_RecomputesDiagnostics()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/IncrementalView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button>\n" +
                            "</UserControl>";
        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        using var initialPublish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        var initialDiagnostics = initialPublish.RootElement.GetProperty("params").GetProperty("diagnostics");
        Assert.True(initialDiagnostics.GetArrayLength() > 0);

        await harness.SendNotificationAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["version"] = 2
            },
            ["contentChanges"] = new JsonArray
            {
                new JsonObject
                {
                    ["range"] = new JsonObject
                    {
                        ["start"] = new JsonObject
                        {
                            ["line"] = 1,
                            ["character"] = 9
                        },
                        ["end"] = new JsonObject
                        {
                            ["line"] = 1,
                            ["character"] = 9
                        }
                    },
                    ["text"] = "/"
                }
            }
        });

        using var updatedPublish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        var updatedDiagnostics = updatedPublish.RootElement.GetProperty("params").GetProperty("diagnostics");
        Assert.True(updatedDiagnostics.GetArrayLength() <= initialDiagnostics.GetArrayLength());
        foreach (var diagnostic in updatedDiagnostics.EnumerateArray())
        {
            if (!diagnostic.TryGetProperty("severity", out var severityElement) ||
                severityElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            Assert.NotEqual(1, severityElement.GetInt32());
        }
    }

    [Fact]
    public async Task DidChange_IncrementalRangeUpdate_ClampsOutOfRangeCharacterToLineEnd()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/IncrementalClampView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button/>\n" +
                            "</UserControl>";
        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        using var initialPublish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        var initialDiagnostics = initialPublish.RootElement.GetProperty("params").GetProperty("diagnostics");
        var initialErrorCount = CountErrorDiagnostics(initialDiagnostics);
        Assert.Equal(0, initialErrorCount);

        await harness.SendNotificationAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["version"] = 2
            },
            ["contentChanges"] = new JsonArray
            {
                new JsonObject
                {
                    ["range"] = new JsonObject
                    {
                        ["start"] = new JsonObject
                        {
                            ["line"] = 1,
                            ["character"] = 999
                        },
                        ["end"] = new JsonObject
                        {
                            ["line"] = 1,
                            ["character"] = 999
                        }
                    },
                    ["text"] = "<!--x-->"
                }
            }
        });

        using var updatedPublish = await harness.ReadNotificationAsync("textDocument/publishDiagnostics");
        var updatedDiagnostics = updatedPublish.RootElement.GetProperty("params").GetProperty("diagnostics");
        var updatedErrorCount = CountErrorDiagnostics(updatedDiagnostics);
        Assert.Equal(0, updatedErrorCount);
    }

    [Fact]
    public async Task References_Request_WithInvalidIncludeDeclarationType_DefaultsToIncludingDeclarations()
    {
        await using var harness = await LspServerHarness.StartAsync();
        await harness.InitializeAsync();

        const string uri = "file:///tmp/ReferencesInvalidContextView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await harness.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "axaml",
                ["version"] = 1,
                ["text"] = xaml
            }
        });

        await harness.SendRequestAsync(4, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri
            },
            ["position"] = new JsonObject
            {
                ["line"] = 2,
                ["character"] = 45
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = "true"
            }
        });

        using var response = await harness.ReadResponseAsync(4);
        var references = response.RootElement.GetProperty("result");
        Assert.True(references.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task CSharpReferences_Request_ForExpressionProperty_ReturnsXamlLocations()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
        await harness.InitializeAsync();

        await harness.SendRequestAsync(9201, "axsg/csharp/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = fixture.CodeUri
            },
            ["position"] = new JsonObject
            {
                ["line"] = fixture.NamePropertyPosition.Line,
                ["character"] = fixture.NamePropertyPosition.Character
            },
            ["documentText"] = fixture.CodeText
        });

        using var response = await harness.ReadResponseAsync(9201);
        var references = response.RootElement.GetProperty("result");

        Assert.True(references.GetArrayLength() > 0);
        foreach (var reference in references.EnumerateArray())
        {
            var uri = reference.GetProperty("uri").GetString();
            Assert.NotNull(uri);
            Assert.True(uri!.EndsWith(".axaml", StringComparison.Ordinal) || uri.EndsWith(".xaml", StringComparison.Ordinal));
        }

        Assert.Contains(references.EnumerateArray(), item =>
            string.Equals(item.GetProperty("uri").GetString(), fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CSharpReferences_Request_ForExpressionMethod_ReturnsXamlLocations()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
        await harness.InitializeAsync();

        await harness.SendRequestAsync(9202, "axsg/csharp/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = fixture.CodeUri
            },
            ["position"] = new JsonObject
            {
                ["line"] = fixture.GetNameMethodPosition.Line,
                ["character"] = fixture.GetNameMethodPosition.Character
            },
            ["documentText"] = fixture.CodeText
        });

        using var response = await harness.ReadResponseAsync(9202);
        var references = response.RootElement.GetProperty("result");

        Assert.True(references.GetArrayLength() > 0);
        Assert.Contains(references.EnumerateArray(), item =>
            string.Equals(item.GetProperty("uri").GetString(), fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CSharpReferences_Request_ForType_ReturnsXamlLocations()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
        await harness.InitializeAsync();

        await harness.SendRequestAsync(9203, "axsg/csharp/references", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = fixture.CodeUri
            },
            ["position"] = new JsonObject
            {
                ["line"] = fixture.ViewModelTypePosition.Line,
                ["character"] = fixture.ViewModelTypePosition.Character
            },
            ["documentText"] = fixture.CodeText
        });

        using var response = await harness.ReadResponseAsync(9203);
        var references = response.RootElement.GetProperty("result");

        Assert.True(references.GetArrayLength() > 0);
        Assert.Contains(references.EnumerateArray(), item =>
            string.Equals(item.GetProperty("uri").GetString(), fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CSharpDeclarations_Request_ForRootViewType_ReturnsXamlXClassLocation()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
        await harness.InitializeAsync();

        await harness.SendRequestAsync(9204, "axsg/csharp/declarations", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = fixture.CodeUri
            },
            ["position"] = new JsonObject
            {
                ["line"] = fixture.MainViewTypePosition.Line,
                ["character"] = fixture.MainViewTypePosition.Character
            },
            ["documentText"] = fixture.CodeText
        });

        using var response = await harness.ReadResponseAsync(9204);
        var declarations = response.RootElement.GetProperty("result");

        Assert.True(declarations.GetArrayLength() > 0);
        Assert.Contains(declarations.EnumerateArray(), item =>
            string.Equals(item.GetProperty("uri").GetString(), fixture.XamlUri, StringComparison.Ordinal));
    }

    private static int CountErrorDiagnostics(JsonElement diagnostics)
    {
        var count = 0;
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            if (!diagnostic.TryGetProperty("severity", out var severityElement) ||
                severityElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (severityElement.GetInt32() == 1)
            {
                count++;
            }
        }

        return count;
    }

    private static Compilation CreateAdHocCompilation(string source, string assemblyName, string filePath)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        ];

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class TogglingCompilationProvider : ICompilationProvider
    {
        private readonly Compilation _initialCompilation;
        private readonly Compilation _updatedCompilation;
        private int _invalidated;

        public TogglingCompilationProvider(Compilation initialCompilation, Compilation updatedCompilation)
        {
            _initialCompilation = initialCompilation;
            _updatedCompilation = updatedCompilation;
        }

        public int InvalidateCalls => _invalidated;

        public Task<CompilationSnapshot> GetCompilationAsync(
            string filePath,
            string? workspaceRoot,
            CancellationToken cancellationToken)
        {
            var compilation = Volatile.Read(ref _invalidated) > 0
                ? _updatedCompilation
                : _initialCompilation;

            return Task.FromResult(new CompilationSnapshot(
                ProjectPath: workspaceRoot,
                Project: null,
                Compilation: compilation,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
        }

        public void Invalidate(string filePath)
        {
            Interlocked.Increment(ref _invalidated);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingInvalidateCompilationProvider : ICompilationProvider
    {
        private readonly Compilation _compilation;

        public ThrowingInvalidateCompilationProvider(Compilation compilation)
        {
            _compilation = compilation;
        }

        public Task<CompilationSnapshot> GetCompilationAsync(
            string filePath,
            string? workspaceRoot,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CompilationSnapshot(
                ProjectPath: workspaceRoot,
                Project: null,
                Compilation: _compilation,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
        }

        public void Invalidate(string filePath)
        {
            throw new DirectoryNotFoundException("Could not find a part of the path '" + filePath + "'.");
        }

        public void Dispose()
        {
        }
    }

    private sealed class LspServerHarness : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly Stream _clientWriteStream;
        private readonly Stream _serverReadStream;
        private readonly Stream _serverWriteStream;
        private readonly Stream _clientReadStream;
        private readonly LspMessageReader _clientReader;
        private readonly AxsgLanguageServer _server;
        private readonly CancellationTokenSource _cts;
        private readonly Task<int> _runTask;
        private bool _stopped;

        private readonly string _workspaceRoot;

        private LspServerHarness(ICompilationProvider? compilationProvider = null, string? workspaceRoot = null, TimeSpan? timeout = null)
        {
            _cts = timeout.HasValue
                ? new CancellationTokenSource(timeout.Value)
                : new CancellationTokenSource(TimeSpan.FromSeconds(120));
            _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot) ? "/tmp" : workspaceRoot;
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new LspMessageReader(_clientReadStream);

            var engine = new XamlLanguageServiceEngine(
                LanguageServiceTestCompilationFactory.CreateHarnessCompilationProvider(compilationProvider));
            _server = new AxsgLanguageServer(
                new LspMessageReader(_serverReadStream),
                new LspMessageWriter(_serverWriteStream),
                engine,
                new XamlLanguageServiceOptions(_workspaceRoot));
            _runTask = _server.RunAsync(_cts.Token);
        }

        public static Task<LspServerHarness> StartAsync(ICompilationProvider? compilationProvider = null, string? workspaceRoot = null, TimeSpan? timeout = null)
        {
            return Task.FromResult(new LspServerHarness(compilationProvider, workspaceRoot, timeout));
        }

        public async Task InitializeAsync()
        {
            await SendRequestAsync(100, "initialize", new JsonObject
            {
                ["processId"] = null,
                ["rootUri"] = new Uri(_workspaceRoot).AbsoluteUri,
                ["capabilities"] = new JsonObject()
            });

            using var _ = await ReadResponseAsync(100);
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

        public Task SendRequestAsync(string id, string method, JsonObject parameters)
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

        public Task OpenDocumentAsync(string uri, string text, string languageId = "axaml", int version = 1)
        {
            return SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri,
                    ["languageId"] = languageId,
                    ["version"] = version,
                    ["text"] = text
                }
            });
        }

        public Task CloseDocumentAsync(string uri)
        {
            return SendNotificationAsync("textDocument/didClose", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                }
            });
        }

        public Task SendRawRequestAsync(JsonObject payload)
        {
            return SendAsync(payload);
        }

        public async Task<JsonDocument> ReadResponseAsync(int id)
        {
            return await ReadMatchingMessageAsync(document =>
            {
                var root = document.RootElement;
                return root.TryGetProperty("id", out var idElement) &&
                       idElement.ValueKind == JsonValueKind.Number &&
                       idElement.GetInt32() == id;
            }).ConfigureAwait(false);
        }

        public async Task<JsonDocument> ReadResponseAsync(string id)
        {
            return await ReadMatchingMessageAsync(document =>
            {
                var root = document.RootElement;
                return root.TryGetProperty("id", out var idElement) &&
                       idElement.ValueKind == JsonValueKind.String &&
                       string.Equals(idElement.GetString(), id, StringComparison.Ordinal);
            }).ConfigureAwait(false);
        }

        public async Task<JsonDocument> ReadNotificationAsync(string method)
        {
            return await ReadMatchingMessageAsync(document =>
            {
                var root = document.RootElement;
                return root.TryGetProperty("method", out var methodElement) &&
                       string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
            }).ConfigureAwait(false);
        }

        public async Task<JsonDocument> ReadErrorResponseWithObjectIdAsync(string key, string value)
        {
            return await ReadMatchingMessageAsync(document =>
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!idElement.TryGetProperty(key, out var keyElement) ||
                    keyElement.ValueKind != JsonValueKind.String ||
                    !string.Equals(keyElement.GetString(), value, StringComparison.Ordinal))
                {
                    return false;
                }

                return root.TryGetProperty("error", out _);
            }).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_stopped)
                {
                    await SendRequestAsync(9990, "shutdown", new JsonObject()).ConfigureAwait(false);
                    using var _ = await ReadResponseAsync(9990).ConfigureAwait(false);
                    await SendNotificationAsync("exit", new JsonObject()).ConfigureAwait(false);
                    _stopped = true;
                }
            }
            catch
            {
            }

            _cts.Cancel();

            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch
            {
            }

            _server.Dispose();
            _cts.Dispose();
            _clientWriteStream.Dispose();
            _serverReadStream.Dispose();
            _serverWriteStream.Dispose();
            _clientReadStream.Dispose();
            await _clientToServer.Writer.CompleteAsync().ConfigureAwait(false);
            await _clientToServer.Reader.CompleteAsync().ConfigureAwait(false);
            await _serverToClient.Writer.CompleteAsync().ConfigureAwait(false);
            await _serverToClient.Reader.CompleteAsync().ConfigureAwait(false);
        }

        private async Task SendAsync(JsonObject payload)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(payload);
            var header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");

            await _clientWriteStream.WriteAsync(header, _cts.Token).ConfigureAwait(false);
            await _clientWriteStream.WriteAsync(body, _cts.Token).ConfigureAwait(false);
            await _clientWriteStream.FlushAsync(_cts.Token).ConfigureAwait(false);
        }

        private async Task<JsonDocument> ReadMatchingMessageAsync(Func<JsonDocument, bool> predicate)
        {
            while (true)
            {
                var message = await _clientReader.ReadMessageAsync(_cts.Token).ConfigureAwait(false);
                if (message is null)
                {
                    throw new InvalidOperationException("LSP server stream closed before expected message was received.");
                }

                if (predicate(message))
                {
                    return message;
                }

                message.Dispose();
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var solutionPath = Path.Combine(directory, "XamlToCSharpGenerator.slnx");
            if (File.Exists(solutionPath))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static async Task<LspServerHarness> CreateSharedRepositoryHarnessAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var harness = await LspServerHarness.StartAsync(
            LanguageServiceTestCompilationFactory.CreateSharedMsBuildCompilationProvider(),
            repositoryRoot,
            TimeSpan.FromMinutes(10));
        await harness.InitializeAsync();
        return harness;
    }

    private static string? GetQueryParameter(string uri, string key)
    {
        var query = new Uri(uri).Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(segment.Substring(0, separatorIndex));
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            return Uri.UnescapeDataString(segment.Substring(separatorIndex + 1));
        }

        return null;
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
            throw new InvalidOperationException("Failed to emit metadata compilation for external-controls integration test.");
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

    private static async Task<CrossLanguageNavigationFixture> CreateCrossLanguageNavigationFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-lsp-cross-nav-" + Guid.NewGuid().ToString("N"));
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
            GetPosition(codeText, codeText.IndexOf("Name { get;", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("GetName()", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainViewModel", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainView", StringComparison.Ordinal) + 2),
            new Uri(xamlPath).AbsoluteUri);
    }

    private static async Task<RenameProjectFixture> CreateRenameProjectFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-lsp-rename-" + Guid.NewGuid().ToString("N"));
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
            GetPosition(codeText, codeText.IndexOf("GetName()", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainViewModel", StringComparison.Ordinal) + 2),
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("Name", StringComparison.Ordinal) + 2));
    }

    private static async Task<XClassPartialProjectFixture> CreateXClassPartialProjectFixtureAsync(bool isPartial)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-lsp-xclass-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "MainView.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        string classDeclaration = isPartial
            ? "    public partial class MainView : TestApp.Controls.UserControl { }\n"
            : "    public class MainView : TestApp.Controls.UserControl { }\n";

        string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"using:TestApp\", \"TestApp\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n" +
            "}\n\n" +
            "namespace TestApp\n" +
            "{\n" +
            classDeclaration +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             x:Class=\"TestApp.MainView\" />\n";

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

        return new XClassPartialProjectFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("MainView", StringComparison.Ordinal) + 2));
    }

    private static async Task<EventHandlerProjectFixture> CreateEventHandlerProjectFixtureAsync(bool withIncompatibleHandler)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-lsp-event-handler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "MainView.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        string handlerDeclaration = withIncompatibleHandler
            ? "\n    private void OnClick() { }\n"
            : "\n";

        string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"using:TestApp\", \"TestApp\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n\n" +
            "    public class Button\n" +
            "    {\n" +
            "        public event EventHandler? Click;\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp\n" +
            "{\n" +
            "    public partial class MainView : TestApp.Controls.UserControl\n" +
            "    {" +
            handlerDeclaration +
            "    }\n" +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             x:Class=\"TestApp.MainView\">\n" +
            "  <Button Click=\"OnClick\" />\n" +
            "</UserControl>\n";

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

        return new EventHandlerProjectFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("Click", StringComparison.Ordinal) + 2));
    }

    private static async Task<MissingIncludeProjectFixture> CreateMissingIncludeProjectFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-lsp-missing-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "Themes"));

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "Types.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");
        var includedPath = Path.Combine(rootPath, "Themes", "Shared.axaml");

        const string projectText =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>";

        const string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n" +
            "    public class Styles { }\n" +
            "    public class StyleInclude\n" +
            "    {\n" +
            "        public string Source { get; set; } = string.Empty;\n" +
            "    }\n" +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <UserControl.Styles>\n" +
            "    <StyleInclude Source=\"/Themes/Shared.axaml\" />\n" +
            "  </UserControl.Styles>\n" +
            "</UserControl>\n";

        await File.WriteAllTextAsync(projectPath, projectText);
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);
        await File.WriteAllTextAsync(includedPath, "<Styles xmlns=\"https://github.com/avaloniaui\" />\n");

        return new MissingIncludeProjectFixture(
            rootPath,
            new Uri(projectPath).AbsoluteUri,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("StyleInclude", StringComparison.Ordinal) + 2));
    }

    private static async Task<InvalidIncludeProjectFixture> CreateInvalidIncludeProjectFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-lsp-invalid-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "Types.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        const string projectText =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>";

        const string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n" +
            "    public class Styles { }\n" +
            "    public class StyleInclude\n" +
            "    {\n" +
            "        public string Source { get; set; } = string.Empty;\n" +
            "    }\n" +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <UserControl.Styles>\n" +
            "    <StyleInclude />\n" +
            "  </UserControl.Styles>\n" +
            "</UserControl>\n";

        await File.WriteAllTextAsync(projectPath, projectText);
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);

        return new InvalidIncludeProjectFixture(
            rootPath,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("StyleInclude", StringComparison.Ordinal) + 2));
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

    private static string GetRangeText(string text, JsonElement range)
    {
        var sourceText = SourceText.From(text);
        var start = range.GetProperty("start");
        var end = range.GetProperty("end");
        var startOffset = sourceText.Lines.GetPosition(new LinePosition(
            start.GetProperty("line").GetInt32(),
            start.GetProperty("character").GetInt32()));
        var endOffset = sourceText.Lines.GetPosition(new LinePosition(
            end.GetProperty("line").GetInt32(),
            end.GetProperty("character").GetInt32()));
        return text.Substring(startOffset, endOffset - startOffset);
    }

    private sealed record RenameProjectFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        SourcePosition CodeNamePosition,
        SourcePosition CodeMethodPosition,
        SourcePosition CodeTypePosition,
        string XamlUri,
        string XamlText,
        SourcePosition XamlNamePosition);

    private sealed record XClassPartialProjectFixture(
        string RootPath,
        string CodeUri,
        string XamlUri,
        string XamlText,
        SourcePosition XamlClassPosition);

    private sealed record EventHandlerProjectFixture(
        string RootPath,
        string CodeUri,
        string XamlUri,
        string XamlText,
        SourcePosition EventHandlerPosition);

    private sealed record MissingIncludeProjectFixture(
        string RootPath,
        string ProjectUri,
        string XamlUri,
        string XamlText,
        SourcePosition IncludePosition);

    private sealed record InvalidIncludeProjectFixture(
        string RootPath,
        string XamlUri,
        string XamlText,
        SourcePosition IncludePosition);

    private sealed record CrossLanguageNavigationFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        SourcePosition NamePropertyPosition,
        SourcePosition GetNameMethodPosition,
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
