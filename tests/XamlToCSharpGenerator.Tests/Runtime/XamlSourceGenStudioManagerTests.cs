using System;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenStudioManagerTests
{
    [Fact]
    public void Enable_StartSession_And_StopSession_Update_StatusSnapshot()
    {
        ResetManagers();

        var options = new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false,
            WaitMode = SourceGenStudioWaitMode.None,
            FallbackPolicy = SourceGenStudioFallbackPolicy.NoFallback
        };

        XamlSourceGenStudioManager.Enable(options);
        var sessionId = XamlSourceGenStudioManager.StartSession();

        var activeSnapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(activeSnapshot.IsEnabled);
        Assert.Equal(sessionId, activeSnapshot.SessionId);
        Assert.Equal(SourceGenStudioOperationState.Ready, activeSnapshot.CurrentState);

        XamlSourceGenStudioManager.StopSession();

        var stoppedSnapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(stoppedSnapshot.IsEnabled);
        Assert.Equal(Guid.Empty, stoppedSnapshot.SessionId);
        Assert.Empty(stoppedSnapshot.Operations);
    }

    [Fact]
    public async Task ApplyUpdateAsync_Records_Operation_And_Raises_Events()
    {
        ResetManagers();

        var target = new StudioTarget();
        XamlSourceGenHotDesignManager.Register(
            target,
            static instance => ((StudioTarget)instance).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/StudioTarget.axaml"
            });

        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            PersistChangesToSource = false,
            WaitMode = SourceGenStudioWaitMode.None,
            FallbackPolicy = SourceGenStudioFallbackPolicy.NoFallback,
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });

        SourceGenStudioOperationStatus? started = null;
        SourceGenStudioOperationStatus? completed = null;
        XamlSourceGenStudioManager.StudioOperationStarted += OnOperationStarted;
        XamlSourceGenStudioManager.StudioOperationCompleted += OnOperationCompleted;

        try
        {
            var result = await XamlSourceGenStudioManager.ApplyUpdateAsync(new SourceGenStudioUpdateRequest
            {
                BuildUri = "avares://tests/StudioTarget.axaml",
                XamlText = "<TextBlock Text=\"Studio\"/>"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(1, target.ApplyCount);
            Assert.NotNull(started);
            Assert.NotNull(completed);
            Assert.Equal(started!.OperationId, completed!.OperationId);
            Assert.Equal(SourceGenStudioOperationState.Succeeded, completed.State);

            var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
            Assert.True(snapshot.IsEnabled);
            Assert.Equal(SourceGenStudioOperationState.Ready, snapshot.CurrentState);
            Assert.NotEmpty(snapshot.Operations);
        }
        finally
        {
            XamlSourceGenStudioManager.StudioOperationStarted -= OnOperationStarted;
            XamlSourceGenStudioManager.StudioOperationCompleted -= OnOperationCompleted;
        }

        return;

        void OnOperationStarted(SourceGenStudioOperationStatus status)
        {
            started = status;
        }

        void OnOperationCompleted(SourceGenStudioOperationStatus status)
        {
            completed = status;
        }
    }

    [Fact]
    public void GetScopes_Uses_Artifact_Metadata_For_Template_Documents()
    {
        ResetManagers();

        XamlSourceGenHotDesignManager.Register(
            new StudioTarget(),
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/Template.axaml",
                ArtifactKind = SourceGenHotDesignArtifactKind.Template,
                DocumentRole = SourceGenHotDesignDocumentRole.Template
            });

        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false
        });

        var scope = Assert.Single(XamlSourceGenStudioManager.GetScopes());
        Assert.Equal(SourceGenStudioScopeKind.Template, scope.ScopeKind);
        Assert.Equal("avares://tests/Template.axaml", scope.BuildUri);
    }

    private static void ResetManagers()
    {
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.StopSession();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
    }

    private sealed class StudioTarget
    {
        public int ApplyCount { get; set; }
    }
}
