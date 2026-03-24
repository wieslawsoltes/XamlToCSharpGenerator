using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class AxsgRuntimeQueryServiceTests
{
    [Fact]
    public void GetHotReloadStatus_Reflects_Registered_Runtime_State()
    {
        ResetRuntimeState();
        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.Register(
            new RuntimeQueryHotReloadTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeQueryHotReloadTarget.axaml",
                SourcePath = "/tmp/RuntimeQueryHotReloadTarget.axaml"
            });

        var service = new AxsgRuntimeQueryService();
        var status = service.GetHotReloadStatus();

        Assert.True(status.IsEnabled);
        Assert.Equal(1, status.RegisteredTypeCount);
        Assert.Equal(1, status.RegisteredBuildUriCount);
    }

    [Fact]
    public void GetHotDesignStatus_And_Documents_Reflect_Registered_Runtime_State()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new RuntimeQueryHotDesignTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeQueryHotDesignTarget.axaml",
                SourcePath = "/tmp/RuntimeQueryHotDesignTarget.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        var service = new AxsgRuntimeQueryService();
        var status = service.GetHotDesignStatus();
        var documents = service.GetHotDesignDocuments();

        Assert.True(status.IsEnabled);
        Assert.Equal(1, status.RegisteredDocumentCount);
        Assert.Single(documents);
        Assert.Equal("avares://tests/RuntimeQueryHotDesignTarget.axaml", documents[0].BuildUri);
    }

    [Fact]
    public void GetStudioStatus_Returns_Current_Studio_Snapshot()
    {
        ResetRuntimeState();
        XamlSourceGenStudioManager.Enable();

        var service = new AxsgRuntimeQueryService();
        var snapshot = service.GetStudioStatus();

        Assert.True(snapshot.IsEnabled);
    }

    private static void ResetRuntimeState()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
    }

    private sealed class RuntimeQueryHotReloadTarget;

    private sealed class RuntimeQueryHotDesignTarget;
}
