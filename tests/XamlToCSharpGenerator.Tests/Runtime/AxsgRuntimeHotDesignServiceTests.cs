using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class AxsgRuntimeHotDesignServiceTests
{
    [Fact]
    public void SelectDocument_Returns_Updated_Workspace_For_Registered_Document()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        const string buildUri = "avares://tests/HotDesignServiceTarget.axaml";
        XamlSourceGenHotDesignManager.Enable();
        XamlSourceGenHotDesignManager.Register(
            new HotDesignServiceTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = buildUri,
                SourcePath = "/tmp/HotDesignServiceTarget.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Root,
                ArtifactKind = SourceGenHotDesignArtifactKind.View
            });

        var service = new AxsgRuntimeHotDesignService(new AxsgRuntimeQueryService());
        SourceGenHotDesignWorkspaceSnapshot workspace = service.SelectDocument(buildUri);

        Assert.Equal(buildUri, workspace.ActiveBuildUri);
    }

    [Fact]
    public void SelectDocument_Without_BuildUri_Throws()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeHotDesignService(new AxsgRuntimeQueryService());

        var exception = Assert.Throws<InvalidOperationException>(() => service.SelectDocument(null));

        Assert.Equal("buildUri is required.", exception.Message);
    }

    [Fact]
    public async Task ApplyDocumentTextAsync_Without_Active_Document_Throws()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeHotDesignService(new AxsgRuntimeQueryService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ApplyDocumentTextAsync(null, "<UserControl />"));

        Assert.Equal("buildUri is required.", exception.Message);
    }

    private sealed class HotDesignServiceTarget;
}
