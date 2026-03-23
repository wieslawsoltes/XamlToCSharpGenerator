using XamlToCSharpGenerator.PreviewerHost;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class PreviewSessionTests
{
    [Fact]
    public void BuildHostProcessArguments_Includes_Axsg_Arguments_For_Bundled_Designer_Host()
    {
        AxsgPreviewHostStartRequest request = CreateStartRequest(
            previewerToolPath: "/tmp/XamlToCSharpGenerator.Previewer.DesignerHost.dll",
            runtimeConfigPath: "/tmp/XamlToCSharpGenerator.Previewer.DesignerHost.runtimeconfig.json",
            depsFilePath: "/tmp/XamlToCSharpGenerator.Previewer.DesignerHost.deps.json");

        IReadOnlyList<string> arguments = PreviewSession.BuildHostProcessArguments(
            request,
            "tcp-bson://127.0.0.1:30303/",
            "session-1",
            "http://127.0.0.1:40404",
            "127.0.0.1",
            50505);

        Assert.Contains("--axsg-compiler-mode", arguments);
        Assert.Contains("--axsg-source-assembly", arguments);
        Assert.Contains("--axsg-design-port", arguments);
        Assert.Equal(request.HostAssemblyPath, arguments[^1]);
    }

    [Fact]
    public void BuildHostProcessArguments_Omits_Axsg_Arguments_For_Project_Host_Fallback()
    {
        AxsgPreviewHostStartRequest request = CreateStartRequest(
            previewerToolPath: "/tmp/Avalonia.Designer.HostApp.dll",
            runtimeConfigPath: "/tmp/App.runtimeconfig.json",
            depsFilePath: "/tmp/App.deps.json");

        IReadOnlyList<string> arguments = PreviewSession.BuildHostProcessArguments(
            request,
            "tcp-bson://127.0.0.1:30303/",
            "session-1",
            "http://127.0.0.1:40404",
            "127.0.0.1",
            50505);

        Assert.DoesNotContain("--axsg-compiler-mode", arguments);
        Assert.DoesNotContain("--axsg-source-assembly", arguments);
        Assert.DoesNotContain("--axsg-design-port", arguments);
        Assert.Equal(request.HostAssemblyPath, arguments[^1]);
    }

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

    [Fact]
    public void SummarizePreviewHostDiagnostics_Prefers_Unhandled_Exception_Message()
    {
        const string diagnostics = """
            Unhandled exception. System.TypeLoadException: Could not load type 'Example.MissingType' from assembly 'Example.Assembly'.
               at Example.Render()
               at Example.Run()
            """;

        string? summary = PreviewSession.SummarizePreviewHostDiagnostics(diagnostics);

        Assert.Equal(
            "System.TypeLoadException: Could not load type 'Example.MissingType' from assembly 'Example.Assembly'.",
            summary);
    }

    [Fact]
    public void BuildHostExitMessage_Includes_Crash_Summary_When_Available()
    {
        string message = PreviewSession.BuildHostExitMessage(
            134,
            "System.TypeLoadException: Could not load type 'Example.MissingType'.");

        Assert.Equal(
            "Preview host crashed (134): System.TypeLoadException: Could not load type 'Example.MissingType'.",
            message);
    }

    [Fact]
    public void BuildProjectHostFallbackDesignUnavailableMessage_Explains_Fallback_Limitations()
    {
        string message = PreviewSession.BuildProjectHostFallbackDesignUnavailableMessage();

        Assert.Contains("project host fallback", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bundled AXSG designer host", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AXSG Inspector", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteInitialPreviewStartupAsync_Waits_For_Preview_Url_Before_Sending_Initial_Update()
    {
        var steps = new List<string>();
        var previewUrlAvailable = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> startupTask = PreviewSession.CompleteInitialPreviewStartupAsync(
            cancellationToken =>
            {
                steps.Add("bootstrap");
                return Task.CompletedTask;
            },
            cancellationToken =>
            {
                steps.Add("update");
                return Task.CompletedTask;
            },
            previewUrlAvailable.Task,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(["bootstrap"], steps);

        previewUrlAvailable.SetResult("http://127.0.0.1:7000");

        string previewUrl = await startupTask;

        Assert.Equal("http://127.0.0.1:7000", previewUrl);
        Assert.Equal(["bootstrap", "update"], steps);
    }

    private static AxsgPreviewHostStartRequest CreateStartRequest(
        string previewerToolPath,
        string runtimeConfigPath,
        string depsFilePath)
    {
        return new AxsgPreviewHostStartRequest(
            "dotnet",
            "/tmp/App.dll",
            previewerToolPath,
            runtimeConfigPath,
            depsFilePath,
            "/tmp/Source.dll",
            "/tmp/View.axaml",
            "/Views/View.axaml",
            "<View />",
            "avalonia",
            800,
            600,
            1);
    }
}
