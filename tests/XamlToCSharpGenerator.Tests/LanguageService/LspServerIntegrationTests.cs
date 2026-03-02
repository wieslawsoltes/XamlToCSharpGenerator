using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageServer.Protocol;
using XamlToCSharpGenerator.LanguageServer.Server;

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
        Assert.True(capabilities.GetProperty("referencesProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("documentSymbolProvider").GetBoolean());
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

        private LspServerHarness()
        {
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new LspMessageReader(_clientReadStream);

            var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
            _server = new AxsgLanguageServer(
                new LspMessageReader(_serverReadStream),
                new LspMessageWriter(_serverWriteStream),
                engine,
                new XamlLanguageServiceOptions("/tmp"));
            _runTask = _server.RunAsync(_cts.Token);
        }

        public static Task<LspServerHarness> StartAsync()
        {
            return Task.FromResult(new LspServerHarness());
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
            return await ReadMatchingMessageAsync(document =>
            {
                var root = document.RootElement;
                return root.TryGetProperty("id", out var idElement) &&
                       idElement.ValueKind == JsonValueKind.Number &&
                       idElement.GetInt32() == id;
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
}
