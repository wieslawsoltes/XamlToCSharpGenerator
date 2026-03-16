using System.Text.Json;
using XamlToCSharpGenerator.RemoteProtocol.Studio;

namespace XamlToCSharpGenerator.Tests.Runtime;

public sealed class AxsgStudioRemoteProtocolTests
{
    [Fact]
    public void ParseRequestLine_Uses_MessageType_Fallback_And_Trimmed_RequestId()
    {
        var request = AxsgStudioRemoteProtocol.ParseRequestLine(
            """
            {"messageType":"GETSTATUS","requestId":"  r1  "}
            """);

        Assert.Equal(AxsgStudioRemoteProtocol.GetStatusCommand, request.Command);
        Assert.Equal("r1", request.RequestId);
        Assert.Equal(JsonValueKind.Object, request.Payload.ValueKind);
    }

    [Fact]
    public void ParseApplyDocumentTextRequest_Allows_Empty_Xaml_And_Trimmed_BuildUri()
    {
        var payload = JsonDocument.Parse(
            """
            {"buildUri":"  avares://sample.axaml  ","xamlText":""}
            """).RootElement.Clone();

        var request = AxsgStudioRemoteProtocol.ParseApplyDocumentTextRequest(payload);

        Assert.Equal("avares://sample.axaml", request.BuildUri);
        Assert.Equal(string.Empty, request.XamlText);
    }

    [Fact]
    public void CreateFailureResponse_Preserves_Command_And_Error()
    {
        var response = AxsgStudioRemoteProtocol.CreateFailureResponse(
            AxsgStudioRemoteProtocol.SelectDocumentCommand,
            "42",
            "buildUri is required.");

        Assert.False(response.Ok);
        Assert.Equal(AxsgStudioRemoteProtocol.SelectDocumentCommand, response.Command);
        Assert.Equal("42", response.RequestId);
        Assert.Equal("buildUri is required.", response.Error);
        Assert.Null(response.Payload);
    }
}
