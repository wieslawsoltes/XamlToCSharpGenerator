using System.Collections.Generic;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class AxsgRuntimeHotReloadServiceTests
{
    [Fact]
    public void Enable_And_Disable_Return_Updated_Status()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeHotReloadService();

        SourceGenHotReloadStatus disabled = service.Disable();
        SourceGenHotReloadStatus enabled = service.Enable();

        Assert.False(disabled.IsEnabled);
        Assert.True(enabled.IsEnabled);
    }

    [Fact]
    public void Toggle_Flips_Enablement_State()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeHotReloadService();

        SourceGenHotReloadStatus disabled = service.Disable();
        SourceGenHotReloadStatus enabled = service.Toggle();
        SourceGenHotReloadStatus disabledAgain = service.Toggle();

        Assert.False(disabled.IsEnabled);
        Assert.True(enabled.IsEnabled);
        Assert.False(disabledAgain.IsEnabled);
    }

    [Fact]
    public void GetTrackedDocuments_Returns_Registered_Documents()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        var service = new AxsgRuntimeHotReloadService();

        XamlSourceGenHotReloadManager.Register(
            new HotReloadTrackedDocumentTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/Tracked.axaml",
                SourcePath = "/tmp/Tracked.axaml"
            });

        IReadOnlyList<SourceGenHotReloadTrackedDocumentDescriptor> trackedDocuments = service.GetTrackedDocuments();

        SourceGenHotReloadTrackedDocumentDescriptor document = Assert.Single(trackedDocuments);
        Assert.Equal(typeof(HotReloadTrackedDocumentTarget), document.TrackingType);
        Assert.Equal("avares://tests/Tracked.axaml", document.BuildUri);
        Assert.Equal("/tmp/Tracked.axaml", document.SourcePath);
        Assert.Equal(1, document.LiveInstanceCount);
        Assert.True(document.IsSourceWatched);
    }

    private sealed class HotReloadTrackedDocumentTarget;
}
