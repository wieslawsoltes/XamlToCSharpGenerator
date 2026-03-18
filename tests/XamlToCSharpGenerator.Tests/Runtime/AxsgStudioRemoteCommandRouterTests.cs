using System.Text.Json;
using System.Reflection;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Studio;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public sealed class AxsgStudioRemoteCommandRouterTests
{
    [Fact]
    public async Task HandleAsync_Ping_Returns_Pong()
    {
        var router = new AxsgStudioRemoteCommandRouter(new AxsgRuntimeQueryService());

        var response = await router.HandleAsync(
            new AxsgStudioRemoteRequestEnvelope(
                AxsgStudioRemoteProtocol.PingCommand,
                "p1",
                CreatePayload(new { })),
            CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(AxsgStudioRemoteProtocol.PingCommand, response.Command);
        Assert.NotNull(response.Payload);

        var payload = JsonSerializer.SerializeToElement(response.Payload, JsonRpcSerializer.DefaultOptions);
        Assert.True(payload.GetProperty("pong").GetBoolean());
        Assert.True(payload.TryGetProperty("utcNow", out _));
    }

    [Fact]
    public async Task HandleAsync_SelectDocument_Without_BuildUri_Returns_Error()
    {
        var router = new AxsgStudioRemoteCommandRouter(new AxsgRuntimeQueryService());

        var response = await router.HandleAsync(
            new AxsgStudioRemoteRequestEnvelope(
                AxsgStudioRemoteProtocol.SelectDocumentCommand,
                "s1",
                CreatePayload("invalid")),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("buildUri is required.", response.Error);
    }

    [Fact]
    public async Task HandleAsync_Unsupported_Command_Returns_Failure()
    {
        var router = new AxsgStudioRemoteCommandRouter(new AxsgRuntimeQueryService());

        var response = await router.HandleAsync(
            new AxsgStudioRemoteRequestEnvelope(
                "unsupported",
                null,
                CreatePayload(new { })),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("Unsupported command", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_SelectElement_Without_Active_Document_Returns_Error()
    {
        var router = new AxsgStudioRemoteCommandRouter(new AxsgRuntimeQueryService());

        var response = await router.HandleAsync(
            new AxsgStudioRemoteRequestEnvelope(
                AxsgStudioRemoteProtocol.SelectElementCommand,
                "e1",
                CreatePayload(new
                {
                    elementId = "root"
                })),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("No active document is available for element selection.", response.Error);
    }

    [Fact]
    public async Task HandleAsync_ApplyDocumentText_Without_XamlText_Returns_Error()
    {
        var router = new AxsgStudioRemoteCommandRouter(new AxsgRuntimeQueryService());

        var response = await router.HandleAsync(
            new AxsgStudioRemoteRequestEnvelope(
                AxsgStudioRemoteProtocol.ApplyDocumentTextCommand,
                "a1",
                CreatePayload(new
                {
                    buildUri = "avares://tests/Missing.xaml"
                })),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("xamlText is required.", response.Error);
    }

    [Fact]
    public void BuildHotDesignMutationResponse_ForFailedApplyResult_KeepsPayloadEnvelopeSuccessful()
    {
        var router = new AxsgStudioRemoteCommandRouter(new AxsgRuntimeQueryService());
        var request = new AxsgStudioRemoteRequestEnvelope(
            AxsgStudioRemoteProtocol.RemoveElementCommand,
            "m1",
            CreatePayload(new { }));
        var result = new SourceGenHotDesignApplyResult(
            Succeeded: false,
            Message: "Cannot remove the root element.",
            BuildUri: "/Pages/MainView.axaml");

        MethodInfo method = typeof(AxsgStudioRemoteCommandRouter).GetMethod(
            "BuildHotDesignMutationResponse",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var response = (AxsgStudioRemoteResponseEnvelope)method.Invoke(
            router,
            new object?[] { request, result, result.BuildUri })!;

        Assert.True(response.Ok);
        Assert.Null(response.Error);

        JsonElement payload = JsonSerializer.SerializeToElement(response.Payload, JsonRpcSerializer.DefaultOptions);
        Assert.False(payload.GetProperty("applyResult").GetProperty("succeeded").GetBoolean());
        Assert.Equal("Cannot remove the root element.", payload.GetProperty("applyResult").GetProperty("message").GetString());
    }

    private static JsonElement CreatePayload<T>(T value)
    {
        return JsonSerializer.SerializeToDocument(value, JsonRpcSerializer.DefaultOptions).RootElement.Clone();
    }
}
