using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class AxsgRuntimeStudioServiceTests
{
    [Fact]
    public void Configure_Applies_Full_Studio_Options_Snapshot()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeStudioService(new AxsgRuntimeQueryService());
        service.Enable();

        SourceGenStudioStatusSnapshot snapshot = service.Configure(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = 51234,
            EnableTracing = true,
            CanvasLayoutMode = SourceGenStudioCanvasLayoutMode.Stacked
        });

        Assert.True(snapshot.IsEnabled);
        Assert.Equal(SourceGenStudioCanvasLayoutMode.Stacked, snapshot.Options.CanvasLayoutMode);
        Assert.True(snapshot.Options.EnableRemoteDesign);
        Assert.Equal("127.0.0.1", snapshot.Remote.Host);
        Assert.Equal(51234, snapshot.Remote.Port);
    }

    [Fact]
    public async Task ApplyDocumentTextAsync_Without_Active_Document_Throws()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeStudioService(new AxsgRuntimeQueryService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ApplyDocumentTextAsync(null, "<UserControl />"));

        Assert.Equal("buildUri is required.", exception.Message);
    }

    [Fact]
    public async Task ApplyUpdateAsync_Uses_Active_BuildUri_And_Document_Metadata()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();

        var target = new StudioServiceTarget();
        XamlSourceGenHotDesignManager.Register(
            target,
            static instance => ((StudioServiceTarget)instance).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/AxsgRuntimeStudioServiceTests.axaml",
                SourcePath = "/tmp/AxsgRuntimeStudioServiceTests.axaml",
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
        XamlSourceGenHotDesignTool.SelectDocument("avares://tests/AxsgRuntimeStudioServiceTests.axaml");

        var service = new AxsgRuntimeStudioService(new AxsgRuntimeQueryService());
        SourceGenStudioUpdateResult result = await service.ApplyUpdateAsync(new SourceGenStudioUpdateRequest
        {
            XamlText = "<TextBlock Text=\"Studio MCP\"/>",
            WaitMode = SourceGenStudioWaitMode.None,
            FallbackPolicy = SourceGenStudioFallbackPolicy.NoFallback,
            PersistChangesToSource = false
        });

        Assert.True(result.Succeeded);
        Assert.Equal("avares://tests/AxsgRuntimeStudioServiceTests.axaml", result.BuildUri);
        Assert.Equal(typeof(StudioServiceTarget), result.TargetType);
        Assert.Equal(1, target.ApplyCount);
    }

    private sealed class StudioServiceTarget
    {
        public int ApplyCount { get; set; }
    }
}
