using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.PreviewerHost;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class PreviewHostMcpServerTests
{
    [Fact]
    public async Task Initialize_Advertises_Preview_Subscriptions_And_ListChanged()
    {
        await using var harness = await PreviewHostMcpHarness.StartAsync();

        await harness.SendRequestAsync(
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

        using JsonDocument response = await harness.ReadResponseAsync(1);
        JsonElement capabilities = response.RootElement.GetProperty("result").GetProperty("capabilities");

        Assert.True(capabilities.GetProperty("tools").GetProperty("listChanged").GetBoolean());
        JsonElement resources = capabilities.GetProperty("resources");
        Assert.True(resources.GetProperty("subscribe").GetBoolean());
        Assert.True(resources.GetProperty("listChanged").GetBoolean());
    }

    [Fact]
    public async Task Start_Tool_Publishes_List_Changes_And_Current_Session_Resource()
    {
        await using var harness = await PreviewHostMcpHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            9,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = PreviewHostMcpServer.StatusResourceUri
            });
        using (JsonDocument subscribeResponse = await harness.ReadResponseAsync(9))
        {
        }

        await harness.SendRequestAsync(10, "tools/list", new JsonObject());
        using (JsonDocument initialTools = await harness.ReadResponseAsync(10))
        {
            string[] toolNames = initialTools.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(static item => item.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();

            Assert.Contains("axsg.preview.start", toolNames, StringComparer.Ordinal);
            Assert.DoesNotContain("axsg.preview.hotReload", toolNames, StringComparer.Ordinal);
            Assert.DoesNotContain("axsg.preview.update", toolNames, StringComparer.Ordinal);
            Assert.DoesNotContain("axsg.preview.stop", toolNames, StringComparer.Ordinal);
        }

        await harness.SendRequestAsync(
            11,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.preview.start",
                ["arguments"] = CreateStartArguments()
            });

        using JsonDocument response = await harness.ReadResponseAsync(11);
        JsonElement structuredContent = response.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("http://127.0.0.1:7000", structuredContent.GetProperty("previewUrl").GetString());

        using JsonDocument toolsChanged = await harness.ReadNotificationAsync("notifications/tools/list_changed");
        using JsonDocument resourceUpdated = await harness.ReadNotificationAsync("notifications/resources/updated");

        Assert.NotNull(toolsChanged);
        Assert.Contains(
            resourceUpdated.RootElement.GetProperty("params").GetProperty("uri").GetString(),
            new[]
            {
                PreviewHostMcpServer.StatusResourceUri,
                PreviewHostMcpServer.EventsResourceUri,
                PreviewHostMcpServer.CurrentSessionResourceUri
            });

        await harness.SendRequestAsync(12, "tools/list", new JsonObject());
        using (JsonDocument updatedTools = await harness.ReadResponseAsync(12))
        {
            string[] toolNames = updatedTools.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(static item => item.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();

            Assert.Contains("axsg.preview.update", toolNames, StringComparer.Ordinal);
            Assert.Contains("axsg.preview.hotReload", toolNames, StringComparer.Ordinal);
            Assert.Contains("axsg.preview.stop", toolNames, StringComparer.Ordinal);
        }

        await harness.SendRequestAsync(13, "resources/list", new JsonObject());
        using (JsonDocument resources = await harness.ReadResponseAsync(13))
        {
            string[] resourceUris = resources.RootElement
                .GetProperty("result")
                .GetProperty("resources")
                .EnumerateArray()
                .Select(static item => item.GetProperty("uri").GetString() ?? string.Empty)
                .ToArray();

            Assert.Contains(PreviewHostMcpServer.CurrentSessionResourceUri, resourceUris, StringComparer.Ordinal);
        }

        await harness.SendRequestAsync(
            14,
            "resources/read",
            new JsonObject
            {
                ["uri"] = PreviewHostMcpServer.CurrentSessionResourceUri
            });

        using JsonDocument currentSessionResponse = await harness.ReadResponseAsync(14);
        string currentSessionText = currentSessionResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()!;
        using JsonDocument currentSessionPayload = JsonDocument.Parse(currentSessionText);
        Assert.Equal("http://127.0.0.1:7000", currentSessionPayload.RootElement.GetProperty("previewUrl").GetString());
    }

    [Fact]
    public async Task HostExited_Event_Removes_Dynamic_Catalog_Entries_And_Updates_Status()
    {
        await using var harness = await PreviewHostMcpHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            19,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = PreviewHostMcpServer.StatusResourceUri
            });
        using (JsonDocument subscribeResponse = await harness.ReadResponseAsync(19))
        {
        }

        await harness.SendRequestAsync(
            20,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.preview.start",
                ["arguments"] = CreateStartArguments()
            });
        using JsonDocument startResponse = await harness.ReadResponseAsync(20);

        FakePreviewHostSession session = harness.SingleSession;
        session.RaiseHostExited(9);

        using JsonDocument toolsChanged = await harness.ReadNotificationAsync("notifications/tools/list_changed");
        using JsonDocument resourceUpdated = await harness.ReadNotificationAsync("notifications/resources/updated");

        Assert.NotNull(toolsChanged);
        Assert.Contains(
            resourceUpdated.RootElement.GetProperty("params").GetProperty("uri").GetString(),
            new[]
            {
                PreviewHostMcpServer.StatusResourceUri,
                PreviewHostMcpServer.EventsResourceUri
            });

        await harness.SendRequestAsync(21, "tools/list", new JsonObject());
        using (JsonDocument updatedTools = await harness.ReadResponseAsync(21))
        {
            string[] toolNames = updatedTools.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(static item => item.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain("axsg.preview.update", toolNames, StringComparer.Ordinal);
            Assert.DoesNotContain("axsg.preview.hotReload", toolNames, StringComparer.Ordinal);
            Assert.DoesNotContain("axsg.preview.stop", toolNames, StringComparer.Ordinal);
        }

        await harness.SendRequestAsync(
            22,
            "resources/read",
            new JsonObject
            {
                ["uri"] = PreviewHostMcpServer.StatusResourceUri
            });
        using JsonDocument statusResponse = await harness.ReadResponseAsync(22);
        string statusText = statusResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()!;
        using JsonDocument statusPayload = JsonDocument.Parse(statusText);
        Assert.Equal("exited", statusPayload.RootElement.GetProperty("phase").GetString());
        Assert.Equal(9, statusPayload.RootElement.GetProperty("hostExitCode").GetInt32());

        await harness.SendRequestAsync(23, "resources/list", new JsonObject());
        using JsonDocument resources = await harness.ReadResponseAsync(23);
        string[] resourceUris = resources.RootElement
            .GetProperty("result")
            .GetProperty("resources")
            .EnumerateArray()
            .Select(static item => item.GetProperty("uri").GetString() ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(PreviewHostMcpServer.CurrentSessionResourceUri, resourceUris, StringComparer.Ordinal);
    }

    [Fact]
    public async Task HotReload_Tool_Waits_For_InProcess_Result_And_Updates_Status()
    {
        await using var harness = await PreviewHostMcpHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            30,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.preview.start",
                ["arguments"] = CreateStartArguments()
            });
        using JsonDocument startResponse = await harness.ReadResponseAsync(30);

        await harness.SendRequestAsync(
            31,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.preview.hotReload",
                ["arguments"] = new JsonObject
                {
                    ["xamlText"] = "<UserControl Updated=\"True\" />",
                    ["timeoutMs"] = 2500
                }
            });

        using JsonDocument hotReloadResponse = await harness.ReadResponseAsync(31);
        JsonElement structuredContent = hotReloadResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.True(structuredContent.GetProperty("succeeded").GetBoolean());

        FakePreviewHostSession session = harness.SingleSession;
        Assert.Equal("<UserControl Updated=\"True\" />", session.LastHotReloadXaml);

        await harness.SendRequestAsync(
            32,
            "resources/read",
            new JsonObject
            {
                ["uri"] = PreviewHostMcpServer.StatusResourceUri
            });

        using JsonDocument statusResponse = await harness.ReadResponseAsync(32);
        string statusText = statusResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()!;
        using JsonDocument statusPayload = JsonDocument.Parse(statusText);
        Assert.Equal("running", statusPayload.RootElement.GetProperty("phase").GetString());
        Assert.True(statusPayload.RootElement.GetProperty("lastUpdateSucceeded").GetBoolean());
    }

    private static JsonObject CreateStartArguments()
    {
        return new JsonObject
        {
            ["dotNetCommand"] = "dotnet",
            ["hostAssemblyPath"] = "artifacts/host.dll",
            ["previewerToolPath"] = "artifacts/previewer.dll",
            ["runtimeConfigPath"] = "artifacts/host.runtimeconfig.json",
            ["depsFilePath"] = "artifacts/host.deps.json",
            ["sourceAssemblyPath"] = "artifacts/source.dll",
            ["xamlFileProjectPath"] = "/Pages/MainView.axaml",
            ["xamlText"] = "<UserControl />",
            ["previewCompilerMode"] = "sourceGenerated",
            ["previewWidth"] = 800,
            ["previewHeight"] = 600,
            ["previewScale"] = 2
        };
    }

    private sealed class PreviewHostMcpHarness : IAsyncDisposable
    {
        private readonly object _pendingGate = new();
        private readonly List<JsonDocument> _pendingMessages = new();
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly Stream _clientWriteStream;
        private readonly Stream _serverReadStream;
        private readonly Stream _serverWriteStream;
        private readonly Stream _clientReadStream;
        private readonly JsonRpcMessageReader _clientReader;
        private readonly CancellationTokenSource _cts;
        private readonly Task<int> _runTask;
        private readonly List<FakePreviewHostSession> _sessions = new();
        private readonly PreviewHostMcpServer _server;

        private PreviewHostMcpHarness()
        {
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _clientWriteStream = _clientToServer.Writer.AsStream();
            _serverReadStream = _clientToServer.Reader.AsStream();
            _serverWriteStream = _serverToClient.Writer.AsStream();
            _clientReadStream = _serverToClient.Reader.AsStream();
            _clientReader = new JsonRpcMessageReader(_clientReadStream);
            _server = new PreviewHostMcpServer(
                _serverReadStream,
                _serverWriteStream,
                () =>
                {
                    var session = new FakePreviewHostSession();
                    _sessions.Add(session);
                    return session;
                });
            _runTask = _server.RunAsync(_cts.Token);
        }

        public FakePreviewHostSession SingleSession => Assert.Single(_sessions);

        public static Task<PreviewHostMcpHarness> StartAsync()
        {
            return Task.FromResult(new PreviewHostMcpHarness());
        }

        public async Task InitializeAsync()
        {
            await SendRequestAsync(
                100,
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
            using JsonDocument _ = await ReadResponseAsync(100);
            await SendNotificationAsync("notifications/initialized", new JsonObject());
        }

        public Task SendRequestAsync(int id, string method, JsonObject parameters)
        {
            JsonObject payload = new()
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
            JsonObject payload = new()
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };

            return SendAsync(payload);
        }

        public async Task<JsonDocument> ReadResponseAsync(int id)
        {
            if (TryTakePendingMessage(
                static (document, state) => document.RootElement.TryGetProperty("id", out JsonElement idElement) &&
                                            idElement.GetInt32() == (int)state!,
                id,
                out JsonDocument? pendingDocument))
            {
                return pendingDocument ?? throw new InvalidOperationException("Pending response document was null.");
            }

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
                    QueuePendingMessage(document);
                    continue;
                }

                return document;
            }
        }

        public async Task<JsonDocument> ReadNotificationAsync(string method, int timeoutMs = 5000)
        {
            if (TryTakePendingMessage(
                static (document, state) => document.RootElement.TryGetProperty("method", out JsonElement methodElement) &&
                                            string.Equals(methodElement.GetString(), (string)state!, StringComparison.Ordinal),
                method,
                out JsonDocument? pendingDocument))
            {
                return pendingDocument ?? throw new InvalidOperationException("Pending notification document was null.");
            }

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
                    QueuePendingMessage(document);
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

            await _server.DisposeAsync().ConfigureAwait(false);
            _clientWriteStream.Dispose();
            _serverReadStream.Dispose();
            _serverWriteStream.Dispose();
            _clientReadStream.Dispose();
            DisposePendingMessages();
            _cts.Dispose();
        }

        private Task SendAsync(JsonObject payload)
        {
            var writer = new JsonRpcMessageWriter(_clientWriteStream);
            return writer.WriteAsync(payload, _cts.Token);
        }

        private void QueuePendingMessage(JsonDocument document)
        {
            lock (_pendingGate)
            {
                _pendingMessages.Add(document);
            }
        }

        private bool TryTakePendingMessage(
            Func<JsonDocument, object?, bool> predicate,
            object? state,
            out JsonDocument? document)
        {
            lock (_pendingGate)
            {
                for (int index = 0; index < _pendingMessages.Count; index++)
                {
                    JsonDocument candidate = _pendingMessages[index];
                    if (!predicate(candidate, state))
                    {
                        continue;
                    }

                    _pendingMessages.RemoveAt(index);
                    document = candidate;
                    return true;
                }
            }

            document = null;
            return false;
        }

        private void DisposePendingMessages()
        {
            lock (_pendingGate)
            {
                for (int index = 0; index < _pendingMessages.Count; index++)
                {
                    _pendingMessages[index].Dispose();
                }

                _pendingMessages.Clear();
            }
        }
    }

    private sealed class FakePreviewHostSession : IPreviewHostSession
    {
        public event Action<string>? Log;

        public event Action<string>? PreviewUrlPublished;

        public event Action<AxsgPreviewHostUpdateResultEventPayload>? UpdateCompleted;

        public event Action<int?>? HostExited;

        public string? LastHotReloadXaml { get; private set; }

        public Task<AxsgPreviewHostStartResponse> StartAsync(
            AxsgPreviewHostStartRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AxsgPreviewHostStartResponse(
                "http://127.0.0.1:7000",
                1234,
                7000,
                Guid.Parse("11111111-1111-1111-1111-111111111111")));
        }

        public Task UpdateAsync(string xamlText, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<AxsgPreviewHostHotReloadResponse> HotReloadAsync(
            string xamlText,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastHotReloadXaml = xamlText;
            var result = new AxsgPreviewHostHotReloadResponse(
                true,
                null,
                null,
                DateTimeOffset.UtcNow);
            UpdateCompleted?.Invoke(new AxsgPreviewHostUpdateResultEventPayload(true, null, null));
            return Task.FromResult(result);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void RaiseLog(string message)
        {
            Log?.Invoke(message);
        }

        public void RaisePreviewStarted(string previewUrl)
        {
            PreviewUrlPublished?.Invoke(previewUrl);
        }

        public void RaiseUpdateCompleted(AxsgPreviewHostUpdateResultEventPayload result)
        {
            UpdateCompleted?.Invoke(result);
        }

        public void RaiseHostExited(int? exitCode)
        {
            HostExited?.Invoke(exitCode);
        }
    }
}
