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
    private const string HotDesignStatusResourceUri = "axsg://runtime/hotdesign/status";
    private const string HotReloadEventsResourceUri = "axsg://runtime/hotreload/events";
    private const string HotDesignDocumentsResourceUri = "axsg://runtime/hotdesign/documents";
    private const string HotDesignCurrentWorkspaceResourceUri = "axsg://runtime/hotdesign/workspace/current";
    private const string HotDesignSelectedDocumentResourceUri = "axsg://runtime/hotdesign/document/selected";
    private const string HotDesignSelectedElementResourceUri = "axsg://runtime/hotdesign/element/selected";
    private const string HotDesignEventsResourceUri = "axsg://runtime/hotdesign/events";
    private const string StudioStatusResourceUri = "axsg://runtime/studio/status";
    private const string StudioScopesResourceUri = "axsg://runtime/studio/scopes";
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
        Assert.True(resources.GetProperty("listChanged").GetBoolean());
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
        Assert.Contains("axsg.hotReload.enable", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotReload.disable", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotReload.toggle", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotReload.trackedDocuments", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotReload.remoteTransportStatus", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotReload.lastOperation", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotDesign.workspace", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotDesign.enable", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotDesign.selectDocument", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.hotDesign.setHitTestMode", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.studio.enable", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.studio.configure", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.studio.startSession", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.studio.applyUpdate", toolNames, StringComparer.Ordinal);
        Assert.Contains("axsg.studio.scopes", toolNames, StringComparer.Ordinal);
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
    public async Task ResourcesList_Includes_Focused_Runtime_Snapshot_Resources()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(104, "resources/list", new JsonObject());
        using JsonDocument response = await harness.ReadResponseAsync(104);

        string[] resourceUris = response.RootElement
            .GetProperty("result")
            .GetProperty("resources")
            .EnumerateArray()
            .Select(static item => item.GetProperty("uri").GetString() ?? string.Empty)
            .ToArray();

        Assert.Contains(HotDesignCurrentWorkspaceResourceUri, resourceUris, StringComparer.Ordinal);
        Assert.Contains(HotDesignSelectedDocumentResourceUri, resourceUris, StringComparer.Ordinal);
        Assert.Contains(HotDesignSelectedElementResourceUri, resourceUris, StringComparer.Ordinal);
        Assert.Contains(StudioScopesResourceUri, resourceUris, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ResourcesList_Exposes_PerBuild_HotDesignWorkspace_Resources()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpWorkspaceByBuildUri.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/RuntimeMcpWorkspaceByBuildUri.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(107, "resources/list", new JsonObject());
        using JsonDocument response = await harness.ReadResponseAsync(107);

        string[] resourceUris = response.RootElement
            .GetProperty("result")
            .GetProperty("resources")
            .EnumerateArray()
            .Select(static item => item.GetProperty("uri").GetString() ?? string.Empty)
            .ToArray();

        string resourceUri = CreateHotDesignWorkspaceByBuildUriResourceUri(buildUri);
        Assert.Contains(resourceUri, resourceUris, StringComparer.Ordinal);

        using JsonDocument payload = await ReadResourcePayloadAsync(harness, 1080, resourceUri);
        Assert.Equal(buildUri, payload.RootElement.GetProperty("activeBuildUri").GetString());
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
    public async Task ResourceRead_HotDesignCurrentWorkspace_Returns_Focused_Snapshot()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpCurrentWorkspace.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/RuntimeMcpCurrentWorkspace.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        using JsonDocument payload = await ReadResourcePayloadAsync(harness, 108, HotDesignCurrentWorkspaceResourceUri);
        Assert.Equal(buildUri, payload.RootElement.GetProperty("activeBuildUri").GetString());
    }

    [Fact]
    public async Task ResourceRead_HotDesignSelectedDocument_Returns_Active_Document()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpSelectedDocument.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/RuntimeMcpSelectedDocument.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        using JsonDocument payload = await ReadResourcePayloadAsync(harness, 109, HotDesignSelectedDocumentResourceUri);
        Assert.Equal(buildUri, payload.RootElement.GetProperty("activeBuildUri").GetString());
        Assert.Equal(buildUri, payload.RootElement.GetProperty("document").GetProperty("buildUri").GetString());
    }

    [Fact]
    public async Task ResourceRead_HotDesignSelectedElement_Returns_Focused_Element_Projection()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpSelectedElement.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/RuntimeMcpSelectedElement.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        using JsonDocument payload = await ReadResourcePayloadAsync(harness, 110, HotDesignSelectedElementResourceUri);
        Assert.Equal(buildUri, payload.RootElement.GetProperty("activeBuildUri").GetString());
        Assert.True(payload.RootElement.TryGetProperty("selectedElementId", out JsonElement selectedElementId));
        JsonElement element = payload.RootElement.GetProperty("element");
        if (selectedElementId.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Null)
        {
            Assert.Equal(selectedElementId.GetString(), element.GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task ResourceRead_StudioScopes_Returns_Current_Scopes()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpStudioScopesResource.axaml",
                SourcePath = "/tmp/RuntimeMcpStudioScopesResource.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Template,
                ArtifactKind = SourceGenHotDesignArtifactKind.Template
            });
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        JsonElement[] scopes = await ReadResourceArrayAsync(harness, 111, StudioScopesResourceUri);
        JsonElement scope = Assert.Single(scopes);
        Assert.Equal("Template", scope.GetProperty("scopeKind").GetString());
        Assert.Equal("avares://tests/RuntimeMcpStudioScopesResource.axaml", scope.GetProperty("buildUri").GetString());
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
    public async Task HotDesignDocuments_Change_Publishes_ResourcesListChanged_Notification()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotDesignListChanged.axaml",
                SourcePath = "/tmp/RuntimeMcpHotDesignListChanged.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        using JsonDocument notification = await harness.ReadNotificationAsync("notifications/resources/list_changed");
        Assert.NotNull(notification);
    }

    [Fact]
    public async Task HotDesignWorkspace_Mutation_Publishes_Focused_Workspace_Resource_Notification()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpFocusedWorkspaceUpdate.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/RuntimeMcpFocusedWorkspaceUpdate.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            121,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotDesignCurrentWorkspaceResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(121);

        await harness.SendRequestAsync(
            122,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotDesign.setHitTestMode",
                ["arguments"] = new JsonObject
                {
                    ["mode"] = "Visual"
                }
            });

        using JsonDocument notification = await harness.ReadResourceUpdatedNotificationAsync(HotDesignCurrentWorkspaceResourceUri);
        Assert.Equal(
            HotDesignCurrentWorkspaceResourceUri,
            notification.RootElement.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task DirectHotDesignToolWorkspaceMutation_Publishes_Focused_Workspace_Resource_Notification()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpDirectWorkspaceUpdate.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/RuntimeMcpDirectWorkspaceUpdate.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            123,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotDesignCurrentWorkspaceResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(123);

        XamlSourceGenHotDesignTool.SetCanvasZoom(1.25);

        using JsonDocument notification = await harness.ReadResourceUpdatedNotificationAsync(HotDesignCurrentWorkspaceResourceUri);
        Assert.Equal(
            HotDesignCurrentWorkspaceResourceUri,
            notification.RootElement.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task DirectHotDesignApplyUpdate_Publishes_Focused_Workspace_Resource_Notification()
    {
        ResetRuntimeState();
        const string buildUri = "avares://tests/RuntimeMcpDirectApplyUpdate.axaml";
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = false,
            WaitForHotReload = false
        });
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            124,
            "resources/subscribe",
            new JsonObject
            {
                ["uri"] = HotDesignCurrentWorkspaceResourceUri
            });
        using JsonDocument _ = await harness.ReadResponseAsync(124);

        SourceGenHotDesignApplyResult result = await XamlSourceGenHotDesignTool.ApplyUpdateAsync(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = buildUri,
            XamlText = "<TextBlock Text=\"Updated\"/>"
        });

        Assert.True(result.Succeeded);

        using JsonDocument notification = await harness.ReadResourceUpdatedNotificationAsync(HotDesignCurrentWorkspaceResourceUri);
        Assert.Equal(
            HotDesignCurrentWorkspaceResourceUri,
            notification.RootElement.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task ToolCall_HotDesignEnable_Updates_Status_Resource()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            126,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotDesign.enable",
                ["arguments"] = new JsonObject()
            });
        using JsonDocument response = await harness.ReadResponseAsync(126);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.True(structuredContent.GetProperty("isEnabled").GetBoolean());

        await harness.SendRequestAsync(
            127,
            "resources/read",
            new JsonObject
            {
                ["uri"] = HotDesignStatusResourceUri
            });
        using JsonDocument resourceResponse = await harness.ReadResponseAsync(127);
        string text = resourceResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()!;
        using JsonDocument payload = JsonDocument.Parse(text);
        Assert.True(payload.RootElement.GetProperty("isEnabled").GetBoolean());
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
    public async Task ToolCall_HotReloadEnable_Updates_Status_Resource()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            205,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotReload.enable",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(205);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.True(structuredContent.GetProperty("isEnabled").GetBoolean());
        Assert.True(structuredContent.GetProperty("lastTransportStatus").ValueKind != JsonValueKind.Null);

        await harness.SendRequestAsync(
            206,
            "resources/read",
            new JsonObject
            {
                ["uri"] = HotReloadStatusResourceUri
            });
        using JsonDocument resourceResponse = await harness.ReadResponseAsync(206);
        string text = resourceResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()!;
        using JsonDocument payload = JsonDocument.Parse(text);
        Assert.True(payload.RootElement.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task ToolCall_HotReloadTrackedDocuments_Returns_Registered_Documents()
    {
        ResetRuntimeState();
        XamlSourceGenHotReloadManager.Register(
            new RuntimeMcpHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpTrackedHotReload.axaml",
                SourcePath = "/tmp/RuntimeMcpTrackedHotReload.axaml"
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            207,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotReload.trackedDocuments",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(207);
        JsonElement[] trackedDocuments = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .EnumerateArray()
            .ToArray();

        JsonElement trackedDocument = Assert.Single(trackedDocuments);
        Assert.Equal("avares://tests/RuntimeMcpTrackedHotReload.axaml", trackedDocument.GetProperty("buildUri").GetString());
        Assert.Equal("/tmp/RuntimeMcpTrackedHotReload.axaml", trackedDocument.GetProperty("sourcePath").GetString());
        Assert.Equal(1, trackedDocument.GetProperty("liveInstanceCount").GetInt32());
        Assert.True(trackedDocument.GetProperty("isSourceWatched").GetBoolean());
    }

    [Fact]
    public async Task ToolCall_HotReloadRemoteTransportStatus_Returns_Last_Transport_Status()
    {
        ResetRuntimeState();
        XamlSourceGenHotReloadManager.Enable();

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            208,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotReload.remoteTransportStatus",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(208);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.Equal("Auto", structuredContent.GetProperty("mode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(structuredContent.GetProperty("transportName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(structuredContent.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task ToolCall_HotReloadLastOperation_Returns_Null_Without_Remote_Update()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            209,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotReload.lastOperation",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(209);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.Equal(JsonValueKind.Null, structuredContent.ValueKind);
    }

    [Fact]
    public async Task ToolCall_HotDesignSelectDocument_Returns_Updated_Workspace()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpHotDesignSelectDocument.axaml",
                SourcePath = "/tmp/RuntimeMcpHotDesignSelectDocument.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            210,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotDesign.selectDocument",
                ["arguments"] = new JsonObject
                {
                    ["buildUri"] = "avares://tests/RuntimeMcpHotDesignSelectDocument.axaml"
                }
            });

        using JsonDocument response = await harness.ReadResponseAsync(210);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.Equal(
            "avares://tests/RuntimeMcpHotDesignSelectDocument.axaml",
            structuredContent.GetProperty("activeBuildUri").GetString());
    }

    [Fact]
    public async Task ToolCall_HotDesignSetHitTestMode_Returns_Updated_Workspace_Payload()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable();

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            220,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.hotDesign.setHitTestMode",
                ["arguments"] = new JsonObject
                {
                    ["mode"] = "Visual"
                }
            });

        using JsonDocument response = await harness.ReadResponseAsync(220);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.Equal("Visual", structuredContent.GetProperty("hitTestMode").GetString());
    }

    [Fact]
    public async Task ToolCall_StudioEnable_Updates_Status_Resource()
    {
        ResetRuntimeState();
        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            230,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.studio.enable",
                ["arguments"] = new JsonObject
                {
                    ["showOverlayIndicator"] = false,
                    ["enableExternalWindow"] = false,
                    ["waitMode"] = "None",
                    ["fallbackPolicy"] = "NoFallback"
                }
            });

        using JsonDocument response = await harness.ReadResponseAsync(230);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.True(structuredContent.GetProperty("isEnabled").GetBoolean());

        await harness.SendRequestAsync(
            231,
            "resources/read",
            new JsonObject
            {
                ["uri"] = StudioStatusResourceUri
            });
        using JsonDocument resourceResponse = await harness.ReadResponseAsync(231);
        string text = resourceResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()!;
        using JsonDocument payload = JsonDocument.Parse(text);
        Assert.True(payload.RootElement.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task ToolCall_StudioStartSession_Returns_Updated_Status()
    {
        ResetRuntimeState();
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            240,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.studio.startSession",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(240);
        string? sessionId = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("sessionId")
            .GetString();

        Assert.NotNull(sessionId);
        Assert.NotEqual(Guid.Empty.ToString(), sessionId);
    }

    [Fact]
    public async Task ToolCall_StudioConfigure_Returns_Updated_Status()
    {
        ResetRuntimeState();
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            245,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.studio.configure",
                ["arguments"] = new JsonObject
                {
                    ["canvasLayoutMode"] = "Stacked",
                    ["enableRemoteDesign"] = true,
                    ["remoteHost"] = "127.0.0.1",
                    ["remotePort"] = 45832
                }
            });

        using JsonDocument response = await harness.ReadResponseAsync(245);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.Equal("Stacked", structuredContent.GetProperty("options").GetProperty("canvasLayoutMode").GetString());
        Assert.True(structuredContent.GetProperty("options").GetProperty("enableRemoteDesign").GetBoolean());
        Assert.Equal("127.0.0.1", structuredContent.GetProperty("remote").GetProperty("host").GetString());
        Assert.Equal(45832, structuredContent.GetProperty("remote").GetProperty("port").GetInt32());
    }

    [Fact]
    public async Task ToolCall_StudioScopes_Returns_Runtime_Scopes()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeMcpHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpStudioScopes.axaml",
                SourcePath = "/tmp/RuntimeMcpStudioScopes.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Template,
                ArtifactKind = SourceGenHotDesignArtifactKind.Template
            });
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            250,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.studio.scopes",
                ["arguments"] = new JsonObject()
            });

        using JsonDocument response = await harness.ReadResponseAsync(250);
        JsonElement[] scopes = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .EnumerateArray()
            .ToArray();

        JsonElement scope = Assert.Single(scopes);
        Assert.Equal("Template", scope.GetProperty("scopeKind").GetString());
        Assert.Equal("avares://tests/RuntimeMcpStudioScopes.axaml", scope.GetProperty("buildUri").GetString());
    }

    [Fact]
    public async Task ToolCall_StudioApplyUpdate_Returns_Result_And_Workspace()
    {
        ResetRuntimeState();
        var target = new RuntimeMcpStudioTarget();
        XamlSourceGenHotDesignManager.Register(
            target,
            static instance => ((RuntimeMcpStudioTarget)instance).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeMcpStudioApplyUpdate.axaml",
                SourcePath = "/tmp/RuntimeMcpStudioApplyUpdate.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            PersistChangesToSource = false,
            WaitMode = SourceGenStudioWaitMode.None,
            FallbackPolicy = SourceGenStudioFallbackPolicy.NoFallback,
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });
        XamlSourceGenHotDesignTool.SelectDocument("avares://tests/RuntimeMcpStudioApplyUpdate.axaml");

        await using var harness = await RuntimeMcpServerHarness.StartAsync();
        await harness.InitializeAsync();

        await harness.SendRequestAsync(
            260,
            "tools/call",
            new JsonObject
            {
                ["name"] = "axsg.studio.applyUpdate",
                ["arguments"] = new JsonObject
                {
                    ["xamlText"] = "<TextBlock Text=\"Studio MCP\"/>",
                    ["waitMode"] = "None",
                    ["fallbackPolicy"] = "NoFallback",
                    ["persistChangesToSource"] = false
                }
            });

        using JsonDocument response = await harness.ReadResponseAsync(260);
        JsonElement structuredContent = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");

        Assert.True(structuredContent.GetProperty("applyResult").GetProperty("succeeded").GetBoolean());
        Assert.Equal(
            "avares://tests/RuntimeMcpStudioApplyUpdate.axaml",
            structuredContent.GetProperty("workspace").GetProperty("activeBuildUri").GetString());
        Assert.Equal(1, target.ApplyCount);
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
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
    }

    private sealed class RuntimeMcpHotReloadTarget;

    private sealed class RuntimeMcpHotDesignTarget;

    private sealed class RuntimeMcpStudioTarget
    {
        public int ApplyCount { get; set; }
    }

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

    private static async Task<JsonDocument> ReadResourcePayloadAsync(
        RuntimeMcpServerHarness harness,
        int requestId,
        string uri)
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
        return JsonDocument.Parse(text);
    }

    private static async Task<JsonElement[]> ReadResourceArrayAsync(RuntimeMcpServerHarness harness, int requestId, string uri)
    {
        using JsonDocument payload = await ReadResourcePayloadAsync(harness, requestId, uri);
        return payload.RootElement.EnumerateArray().Select(static item => item.Clone()).ToArray();
    }

    private static string CreateHotDesignWorkspaceByBuildUriResourceUri(string buildUri)
    {
        return "axsg://runtime/hotdesign/workspace/by-build-uri/" + Uri.EscapeDataString(buildUri);
    }
}
