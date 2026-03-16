using System.Text.Json;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class AxsgPreviewHostProtocolTests
{
    [Fact]
    public void ParseCommandLine_Normalizes_Command_Name_And_RequestId()
    {
        var command = AxsgPreviewHostProtocol.ParseCommandLine(
            """
            {"command":"START","requestId":"42","payload":{"xamlText":"<View />"}}
            """);

        Assert.Equal("start", command.Command);
        Assert.Equal("42", command.RequestId);
        Assert.Equal(JsonValueKind.Object, command.Payload.ValueKind);
    }

    [Fact]
    public void ParseCommandLine_Preserves_Canonical_HotReload_Command_Name()
    {
        var command = AxsgPreviewHostProtocol.ParseCommandLine(
            """
            {"command":"hotReload","requestId":"43","payload":{"xamlText":"<View />"}}
            """);

        Assert.Equal(AxsgPreviewHostProtocol.HotReloadCommand, command.Command);
        Assert.Equal("43", command.RequestId);
    }

    [Fact]
    public void ParseStartRequest_Applies_Defaults_And_Normalizes_Paths()
    {
        var payload = JsonDocument.Parse(
            """
            {
              "hostAssemblyPath": "artifacts/host.dll",
              "previewerToolPath": "artifacts/previewer.dll",
              "sourceAssemblyPath": "artifacts/source.dll",
              "xamlFileProjectPath": "/Pages/MainView.axaml",
              "xamlText": ""
            }
            """).RootElement.Clone();

        var request = AxsgPreviewHostProtocol.ParseStartRequest(payload);

        var expectedHostPath = Path.GetFullPath("artifacts/host.dll");
        Assert.Equal("dotnet", request.DotNetCommand);
        Assert.Equal(expectedHostPath, request.HostAssemblyPath);
        Assert.Equal(Path.GetFullPath("artifacts/previewer.dll"), request.PreviewerToolPath);
        Assert.Equal(Path.GetFullPath("artifacts/source.dll"), request.SourceAssemblyPath);
        Assert.Equal(Path.ChangeExtension(expectedHostPath, ".runtimeconfig.json"), request.RuntimeConfigPath);
        Assert.Equal(Path.ChangeExtension(expectedHostPath, ".deps.json"), request.DepsFilePath);
        Assert.Equal("avalonia", request.PreviewCompilerMode);
        Assert.Equal(string.Empty, request.XamlText);
    }

    [Fact]
    public void ParseUpdateRequest_Allows_Empty_Xaml_Text()
    {
        var payload = JsonDocument.Parse("""{"xamlText":""}""").RootElement.Clone();

        var request = AxsgPreviewHostProtocol.ParseUpdateRequest(payload);

        Assert.Equal(string.Empty, request.XamlText);
    }

    [Fact]
    public void ParseHotReloadRequest_Allows_Empty_Xaml_Text_And_Timeout()
    {
        var payload = JsonDocument.Parse("""{"xamlText":"","timeoutMs":2500}""").RootElement.Clone();

        var request = AxsgPreviewHostProtocol.ParseHotReloadRequest(payload);

        Assert.Equal(string.Empty, request.XamlText);
        Assert.Equal(2500, request.TimeoutMs);
    }

    [Fact]
    public void CreateSuccessResponse_Serializes_Strongly_Typed_Payload()
    {
        var response = AxsgPreviewHostProtocol.CreateSuccessResponse(
            "1",
            new AxsgPreviewHostPingResponse(true));

        Assert.Equal("response", response.Kind);
        Assert.Equal("1", response.RequestId);
        Assert.True(response.Ok);
        Assert.True(response.Payload?["pong"]?.GetValue<bool>());
        Assert.Null(response.Error);
    }

    [Fact]
    public void CreateEvent_Serializes_Strongly_Typed_Payload()
    {
        var @event = AxsgPreviewHostProtocol.CreateEvent(
            AxsgPreviewHostProtocol.LogEvent,
            new AxsgPreviewHostLogEventPayload("hello"));

        Assert.Equal("event", @event.Kind);
        Assert.Equal("log", @event.Event);
        Assert.Equal("hello", @event.Payload?["message"]?.GetValue<string>());
    }
}
