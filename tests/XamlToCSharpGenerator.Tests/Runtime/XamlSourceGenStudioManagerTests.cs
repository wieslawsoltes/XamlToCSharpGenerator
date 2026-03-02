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

    [Fact]
    public void Enable_With_RemoteDesign_Initializes_Remote_Status_Snapshot()
    {
        ResetManagers();

        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false,
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = 45831,
            VncEndpoint = "vnc://127.0.0.1:5900"
        });

        var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(snapshot.Remote.IsEnabled);
        Assert.False(snapshot.Remote.IsListening);
        Assert.Equal("127.0.0.1", snapshot.Remote.Host);
        Assert.Equal(45831, snapshot.Remote.Port);
        Assert.Equal("vnc://127.0.0.1:5900", snapshot.Remote.VncEndpoint);
    }

    [Fact]
    public void UpdateRemoteStatus_Overrides_Current_Remote_Status()
    {
        ResetManagers();

        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions
        {
            ShowOverlayIndicator = false,
            EnableExternalWindow = false,
            EnableRemoteDesign = true
        });

        var expected = new SourceGenStudioRemoteStatus(
            IsEnabled: true,
            IsListening: true,
            Host: "0.0.0.0",
            Port: 49991,
            ActiveClientCount: 2,
            LastError: null,
            VncEndpoint: "vnc://10.0.0.2:5900",
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        XamlSourceGenStudioManager.UpdateRemoteStatus(expected);

        var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
        Assert.True(snapshot.Remote.IsListening);
        Assert.Equal(49991, snapshot.Remote.Port);
        Assert.Equal(2, snapshot.Remote.ActiveClientCount);
        Assert.Equal("vnc://10.0.0.2:5900", snapshot.Remote.VncEndpoint);
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
