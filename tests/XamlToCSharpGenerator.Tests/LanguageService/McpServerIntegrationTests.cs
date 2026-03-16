using System;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService;
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
        Assert.Contains(toolNames, static name => string.Equals(name, "axsg.hotDesign.workspace", StringComparison.Ordinal));
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

        private McpServerHarness(string? workspaceRoot = null)
        {
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new JsonRpcMessageReader(_clientReadStream);

            var engine = new XamlLanguageServiceEngine();
            _server = new AxsgMcpServer(
                new JsonRpcMessageReader(_serverReadStream),
                new JsonRpcMessageWriter(_serverWriteStream),
                engine,
                new XamlLanguageServiceOptions(workspaceRoot));
            _runTask = _server.RunAsync(_cts.Token);
        }

        public static Task<McpServerHarness> StartAsync(string? workspaceRoot = null)
        {
            return Task.FromResult(new McpServerHarness(workspaceRoot));
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
}
