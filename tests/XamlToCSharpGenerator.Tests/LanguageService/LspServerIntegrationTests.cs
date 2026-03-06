using System;
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
        Assert.True(capabilities.GetProperty("codeActionProvider").GetProperty("codeActionKinds").GetArrayLength() > 0);
        Assert.True(capabilities.GetProperty("documentSymbolProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("inlayHintProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("semanticTokensProvider").GetProperty("full").GetBoolean());
        Assert.Equal(2, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32());
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
    public async Task Completion_Request_For_Empty_ExpressionBinding_In_SourceGenCatalogSample_ReturnsMembers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "ExpressionBindingsPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var originalText = await File.ReadAllTextAsync(xamlPath);
        const string existingExpression = "<TextBlock Text=\"{= FirstName + ' ' + LastName}\" />";
        const string emptyExpression = "<TextBlock Text=\"{= }\" />";
        Assert.Contains(existingExpression, originalText, StringComparison.Ordinal);
        var xaml = originalText.Replace(existingExpression, emptyExpression, StringComparison.Ordinal);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf(emptyExpression, StringComparison.Ordinal) + "<TextBlock Text=\"{= ".Length);
        var uri = new Uri(xamlPath).AbsoluteUri;

        await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
        await harness.InitializeAsync();

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
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
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
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
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
    public async Task AxsgRename_Request_ForCSharpProperty_ReturnsWorkspaceEdit()
    {
        var project = await CreateRenameProjectFixtureAsync();
        try
        {
            await using var harness = await LspServerHarness.StartAsync(new MsBuildCompilationProvider());
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
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(60));
        private readonly Task<int> _runTask;
        private bool _stopped;

        private LspServerHarness(ICompilationProvider? compilationProvider = null)
        {
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new LspMessageReader(_clientReadStream);

            var engine = new XamlLanguageServiceEngine(
                compilationProvider ?? new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
            _server = new AxsgLanguageServer(
                new LspMessageReader(_serverReadStream),
                new LspMessageWriter(_serverWriteStream),
                engine,
                new XamlLanguageServiceOptions("/tmp"));
            _runTask = _server.RunAsync(_cts.Token);
        }

        public static Task<LspServerHarness> StartAsync(ICompilationProvider? compilationProvider = null)
        {
            return Task.FromResult(new LspServerHarness(compilationProvider));
        }

        public async Task InitializeAsync()
        {
            await SendRequestAsync(100, "initialize", new JsonObject
            {
                ["processId"] = null,
                ["rootUri"] = "file:///tmp",
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
            var solutionPath = Path.Combine(directory, "XamlToCSharpGenerator.sln");
            if (File.Exists(solutionPath))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
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

    private sealed record RenameProjectFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        SourcePosition CodeNamePosition,
        string XamlUri,
        string XamlText,
        SourcePosition XamlNamePosition);
}
