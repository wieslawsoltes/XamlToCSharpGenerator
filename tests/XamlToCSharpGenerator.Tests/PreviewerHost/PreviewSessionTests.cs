using XamlToCSharpGenerator.PreviewerHost;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class PreviewSessionTests
{
    [Theory]
    [InlineData("System.Net.Sockets.SocketException: Address already in use")]
    [InlineData("Only one usage of each socket address (protocol/network address/port) is normally permitted.")]
    [InlineData("bind failed: EADDRINUSE")]
    public void IsPreviewPortBindFailure_Detects_Common_Port_Collision_Markers(string diagnostics)
    {
        Assert.True(PreviewSession.IsPreviewPortBindFailure(diagnostics));
    }

    [Fact]
    public void IsPreviewPortBindFailure_Ignores_Unrelated_Diagnostics()
    {
        Assert.False(PreviewSession.IsPreviewPortBindFailure("Unhandled exception: Xaml parse failed."));
        Assert.False(PreviewSession.IsPreviewPortBindFailure(string.Empty));
    }

    [Fact]
    public void ShouldRetryStartup_Retries_Port_Bind_Failures_Before_Final_Attempt()
    {
        var exception = new TimeoutException("Timed out waiting for the Avalonia preview URL.");

        Assert.True(PreviewSession.ShouldRetryStartup(
            exception,
            "System.Net.Sockets.SocketException: Address already in use",
            attemptNumber: 1));
        Assert.False(PreviewSession.ShouldRetryStartup(
            exception,
            "System.Net.Sockets.SocketException: Address already in use",
            attemptNumber: 3));
        Assert.False(PreviewSession.ShouldRetryStartup(
            exception,
            "Unhandled exception: Xaml parse failed.",
            attemptNumber: 1));
    }
}
