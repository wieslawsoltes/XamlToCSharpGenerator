using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class RuntimeMcpServerIntegrationTests
{
    private const string HotReloadStatusResourceUri = "axsg://runtime/hotreload/status";
    private const string HotReloadEventsResourceUri = "axsg://runtime/hotreload/events";
    private const string HotDesignDocumentsResourceUri = "axsg://runtime/hotdesign/documents";
    private const string HotDesignEventsResourceUri = "axsg://runtime/hotdesign/events";
    private const string StudioEventsResourceUri = "axsg://runtime/studio/events";

    [Fact]
    public async Task Initialize_Advertises_Runtime_Resource_Subscriptions()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();

        await harness.SendRequestAsync(
            10,
            "initialize",
            new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "tests",
                    ["version"] = "1.0.0"
                }
            });

        using JsonDocument response = await harness.ReadResponseAsync(10);
        JsonElement resources = response.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("resources");

        Assert.True(resources.GetProperty("subscribe").GetBoolean());
        Assert.False(resources.GetProperty("listChanged").GetBoolean());
    }

    [Fact]
    public async Task ToolsList_Exposes_Runtime_Tools_Only()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(100, "tools/list", new JsonObject());
        using JsonDocument response = await harness.ReadResponseAsync(100);

        string[] toolNames = response.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

        Assert.Contains("axsg.hotReload.status", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotDesign.workspace", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.studio.status", toolNames, StringComparer.Ordinal);
        Assert.DoesNotContain("axsg.preview.projectContext", toolNames, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ResourcesList_Includes_Runtime_Event_Resources()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(105, "resources/list", new JsonObject());
        using JsonDocument response = await harness.ReadResponseAsync(105);

        string[] resourceUris = response.RootElement
            .GetProperty("result")
            .GetProperty("resources")
            .EnumerateArray()
            .Select(static item => item.GetProperty("uri").GetString() ?? string.Empty)
            .ToArray();

        Assert.Contains(HotReloadEventsResourceUri, resourceUris, StringComparer.Ordinal);
        Assert.Contains(HotDesignEventsResourceUri, resourceUris, StringComparer.Ordinal);
        Assert.Contains(StudioEventsResourceUri, resourceUris, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ResourceRead_HotReloadEvents_Seeds_Current_Status_On_Start()
    {
        ResetRuntimeState();
        XamlSourceGenHotReloadManager.Enable();

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        JsonElement[] events = await ReadResourceArrayAsync(harness, 106, HotReloadEventsResourceUri);
        Assert.NotEmpty(events);
        Assert.Equal("statusChanged", events[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ResourceSubscribe_HotReloadStatus_Publishes_Updated_Notification()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            110,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotReloadStatusResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(110);

        XamlSourceGenHotReloadManager.Register(
            new RuntimeMcpHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotReloadNotification.axaml",
                SourcePath = "/tmp/RuntimeMcpHotReloadNotification.axaml"
            });

        using JsonDocument notification = await harness.ReadResourceUpdatedNotificationAsync(HotReloadStatusResourceUri);
        Assert.Equal(
            HotReloadStatusResourceUri,
            notification.RootElement.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task ResourceUnsubscribe_HotReloadStatus_Suppresses_Updated_Notification()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            115,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotReloadStatusResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(115);

        await harness.SendRequestAsync(
            116,
            "resources/unsubscribe",
            new JsonObject
            {
                ["uri"] = HotReloadStatusResourceUri
            });
        using JsonDocument __ = await harness.ReadResponseAsync(116);

        XamlSourceGenHotReloadManager.Register(
            new RuntimeMcpHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotReloadUnsubscribe.axaml",
                SourcePath = "/tmp/RuntimeMcpHotReloadUnsubscribe.axaml"
            });

        JsonDocument? notification = await harness.TryReadResourceUpdatedNotificationAsync(HotReloadStatusResourceUri, timeoutMs: 250);
        Assert.Null(notification);
    }

    [Fact]
    public async Task ResourceSubscribe_HotDesignDocuments_Publishes_Updated_Notification()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            120,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotDesignDocumentsResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(120);

        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotDesignNotification.axaml",
                SourcePath = "/tmp/RuntimeMcpHotDesignNotification.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        using JsonDocument notification = await harness.ReadResourceUpdatedNotificationAsync(HotDesignDocumentsResourceUri);
        Assert.Equal(
            HotDesignDocumentsResourceUri,
            notification.RootElement.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task ResourceRead_HotReloadEvents_Reflects_Runtime_Event_Log()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        XamlSourceGenHotReloadManager.Register(
            new RuntimeMcpHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotReloadEvents.axaml",
                SourcePath = "/tmp/RuntimeMcpHotReloadEvents.axaml"
            });

        JsonElement[] events = await ReadResourceArrayAsync(harness, 410, HotReloadEventsResourceUri);
        Assert.Contains(events, static item => string.Equals(item.GetProperty("kind").GetString(), "statusChanged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourceRead_HotDesignEvents_Reflects_Runtime_Event_Log()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotDesignEvents.axaml",
                SourcePath = "/tmp/RuntimeMcpHotDesignEvents.axaml"
            });

        JsonElement[] events = await ReadResourceArrayAsync(harness, 420, HotDesignEventsResourceUri);
        Assert.Contains(events, static item => string.Equals(item.GetProperty("kind").GetString(), "documentsChanged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourceRead_StudioEvents_Reflects_Runtime_Event_Log()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });
        XamlSourceGenStudioManager.StartSession();

        JsonElement[] events = await ReadResourceArrayAsync(harness, 430, StudioEventsResourceUri);
        Assert.Contains(events, static item => string.Equals(item.GetProperty("kind").GetString(), "statusChanged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourceSubscribe_HotReloadEvents_Publishes_Updated_Notification()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            440,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotReloadEventsResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(440);

        XamlSourceGenHotReloadManager.Register(
            new RuntimeMcpHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotReloadEventNotification.axaml",
                SourcePath = "/tmp/RuntimeMcpHotReloadEventNotification.axaml"
            });

        using JsonDocument notification = await harness.ReadResourceUpdatedNotificationAsync(HotReloadEventsResourceUri);
        Assert.Equal(
            HotReloadEventsResourceUri,
            notification.RootElement.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task ToolCall_HotReloadStatus_Reflects_Live_Runtime_State()
    {
        ResetRuntimeState();
        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.Register(
            new RuntimeMcpHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotReloadTarget.axaml",
                SourcePath = "/tmp/RuntimeMcpHotReloadTarget.axaml"
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            200,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotReload.status",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(200);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.True(structuredContent.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(1, structuredContent.GetProperty("registeredTypeCount").GetInt32());
        Assert.Equal(1, structuredContent.GetProperty("registeredBuildUriCount").GetInt32());
    }

    [Fact]
    public async Task ResourceRead_HotDesignDocuments_Reflects_Live_Runtime_State()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotDesignTarget.axaml",
                SourcePath = "/tmp/RuntimeMcpHotDesignTarget.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            300,
            "resources/read",
            new JsonObject
            {
                ["uri"] = "axsg://runtime/hotdesign/documents"
            });

        using JsonDocument response = await harness.ReadResponseAsync(300);
        Assert.True(
            response.RootElement.TryGetProperty("result", out JsonElement result),
            response.RootElement.GetRawText());
        Assert.True(
            result.TryGetProperty("contents", out JsonElement contents),
            response.RootElement.GetRawText());
        JsonElement firstContent = contents.EnumerateArray().First();
        string text = firstContent.GetProperty("text").GetString()!;

        using JsonDocument payload = JsonDocument.Parse(text);
        JsonElement documents = payload.RootElement;
        Assert.Equal(JsonValueKind.Array, documents.ValueKind);
        JsonElement[] documentItems = documents.EnumerateArray().ToArray();
        Assert.Single(documentItems);
        Assert.Equal(
            "avares://tests/RuntimeMcpHotDesignTarget.axaml",
            documentItems[0].GetProperty("buildUri").GetString());
    }

    private static void ResetRuntimeState()
    {
        XamlSourceGenHotReloadManager.ResetTestHooks();
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.ResetHandlersToDefaults();
        XamlSourceGenStudioManager.Disable();
        XamlIncludeGraphRegistry.Clear();
        XamlSourceGenArtifactRefreshRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Clear();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    private sealed class RuntimeMcpHotReloadTarget;

    private sealed class RuntimeMcpHotDesignTarget;

    private sealed class RuntimeMcpServerHarness : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly Stream _clientWriteStream;
        private readonly Stream _serverReadStream;
        private readonly Stream _serverWriteStream;
        private readonly Stream _clientReadStream;
        private readonly JsonRpcMessageReader _clientReader;
        private readonly XamlSourceGenRuntimeMcpServer _server;
        private readonly CancellationTokenSource _cts;
        private readonly Task<int> _runTask;

        private RuntimeMcpServerHarness()
        {
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new JsonRpcMessageReader(_clientReadStream);
            _server = new XamlSourceGenRuntimeMcpServer(_serverReadStream, _serverWriteStream);
            _runTask = _server.RunAsync(_cts.Token);
        }

        public static Task<RuntimeMcpServerHarness> StartAsync()
        {
            return Task.FromResult(new RuntimeMcpServerHarness());
        }

        public async Task InitializeAsync()
        {
            await SendRequestAsync(
                1,
                "initialize",
                new JsonObject
                {
                    ["protocolVersion"] = "2025-11-25",
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "tests",
                        ["version"] = "1.0.0"
                    }
                });
            using JsonDocument _ = await ReadResponseAsync(1);
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
                JsonDocument? document = await _clientReader.ReadMessageAsync(_cts.Token);
                if (document is null)
                {
                    throw new EndOfStreamException();
                }

                if (!document.RootElement.TryGetProperty("id", out JsonElement idElement) ||
                    idElement.GetInt32() != id)
                {
                    document.Dispose();
                    continue;
                }

                return document;
            }
        }

        public async Task<JsonDocument> ReadNotificationAsync(string method, int timeoutMs = 5000)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeoutCts.CancelAfter(timeoutMs);

            while (true)
            {
                JsonDocument? document = await _clientReader.ReadMessageAsync(timeoutCts.Token);
                if (document is null)
                {
                    throw new EndOfStreamException();
                }

                if (!document.RootElement.TryGetProperty("method", out JsonElement methodElement) ||
                    !string.Equals(methodElement.GetString(), method, StringComparison.Ordinal))
                {
                    document.Dispose();
                    continue;
                }

                return document;
            }
        }

        public async Task<JsonDocument> ReadResourceUpdatedNotificationAsync(string uri, int timeoutMs = 5000)
        {
            while (true)
            {
                JsonDocument document = await ReadNotificationAsync("notifications/resources/updated", timeoutMs);
                if (!document.RootElement.TryGetProperty("params", out JsonElement paramsElement) ||
                    !paramsElement.TryGetProperty("uri", out JsonElement uriElement) ||
                    !string.Equals(uriElement.GetString(), uri, StringComparison.Ordinal))
                {
                    document.Dispose();
                    continue;
                }

                return document;
            }
        }

        public async Task<JsonDocument?> TryReadResourceUpdatedNotificationAsync(string uri, int timeoutMs = 5000)
        {
            try
            {
                return await ReadResourceUpdatedNotificationAsync(uri, timeoutMs);
            }
            catch (OperationCanceledException)
            {
                return null;
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

    private static async Task<JsonElement[]> ReadResourceArrayAsync(RuntimeMcpServerHarness harness, int requestId, string uri)
    {
        await harness.SendRequestAsync(
            requestId,
            "resources/read",
            new JsonObject
            {
                ["uri"] = uri
            });

        using JsonDocument response = await harness.ReadResponseAsync(requestId);
        JsonElement firstContent = response.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First();
        string text = firstContent.GetProperty("text").GetString()!;

        using JsonDocument payload = JsonDocument.Parse(text);
        return payload.RootElement.EnumerateArray().Select(static item => item.Clone()).ToArray();
    }
}
