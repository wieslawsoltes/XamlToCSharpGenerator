using System.Text.Json;
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

    private static JsonElement CreatePayload<T>(T value)
    {
        return JsonSerializer.SerializeToDocument(value, JsonRpcSerializer.DefaultOptions).RootElement.Clone();
    }
}
