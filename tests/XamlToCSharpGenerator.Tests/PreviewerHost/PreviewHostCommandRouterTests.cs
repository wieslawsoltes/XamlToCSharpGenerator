using System.Text.Json;
using XamlToCSharpGenerator.PreviewerHost;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class PreviewHostCommandRouterTests
{
    [Fact]
    public async Task HandleCommandAsync_Ping_Returns_Pong_Response()
    {
        await using var router = new PreviewHostCommandRouter(() => new FakePreviewHostSession());

        var response = await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.PingCommand,
                "1",
                CreatePayload(new { })),
            CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("1", response.RequestId);
        Assert.True(response.Payload?["pong"]?.GetValue<bool>());
    }

    [Fact]
    public async Task HandleCommandAsync_Update_Without_Start_Returns_Failure()
    {
        await using var router = new PreviewHostCommandRouter(() => new FakePreviewHostSession());

        var response = await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.UpdateCommand,
                "2",
                CreatePayload(new AxsgPreviewHostUpdateRequest("updated"))),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("Preview session has not been started.", response.Error);
    }

    [Fact]
    public async Task HandleCommandAsync_Start_Disposes_Previous_Session_And_Forwards_Events()
    {
        var sessions = new List<FakePreviewHostSession>();
        await using var router = new PreviewHostCommandRouter(() =>
        {
            var session = new FakePreviewHostSession();
            sessions.Add(session);
            return session;
        });

        var events = new List<AxsgPreviewHostEventEnvelope>();
        router.EventPublished += events.Add;

        var firstStart = await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.StartCommand,
                "3",
                CreatePayload(CreateStartRequest("xaml-1"))),
            CancellationToken.None);

        sessions[0].RaiseLog("ready");
        sessions[0].RaisePreviewStarted("http://127.0.0.1:7000");
        sessions[0].RaiseUpdateCompleted(new AxsgPreviewHostUpdateResultEventPayload(true, null, null));
        sessions[0].RaiseHostExited(5);

        var secondStart = await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.StartCommand,
                "4",
                CreatePayload(CreateStartRequest("xaml-2"))),
            CancellationToken.None);

        Assert.True(firstStart.Ok);
        Assert.True(secondStart.Ok);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(1, sessions[0].DisposeCount);
        Assert.Equal("xaml-1", sessions[0].StartedRequest?.XamlText);
        Assert.Equal("xaml-2", sessions[1].StartedRequest?.XamlText);
        Assert.Collection(
            events,
            item => Assert.Equal(AxsgPreviewHostProtocol.LogEvent, item.Event),
            item => Assert.Equal(AxsgPreviewHostProtocol.PreviewStartedEvent, item.Event),
            item => Assert.Equal(AxsgPreviewHostProtocol.UpdateResultEvent, item.Event),
            item => Assert.Equal(AxsgPreviewHostProtocol.HostExitedEvent, item.Event));
    }

    [Fact]
    public async Task HandleCommandAsync_HotReload_Returns_Completed_Result()
    {
        var session = new FakePreviewHostSession();
        await using var router = new PreviewHostCommandRouter(() => session);

        await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.StartCommand,
                "5",
                CreatePayload(CreateStartRequest("xaml-1"))),
            CancellationToken.None);

        AxsgPreviewHostResponseEnvelope response = await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.HotReloadCommand,
                "6",
                CreatePayload(new AxsgPreviewHostHotReloadRequest("updated", 2500))),
            CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("updated", session.LastHotReloadXaml);
        Assert.True(response.Payload?["succeeded"]?.GetValue<bool>());
        Assert.Equal("6", response.RequestId);
    }

    [Fact]
    public async Task HandleCommandAsync_Input_Forwards_Request()
    {
        var session = new FakePreviewHostSession();
        await using var router = new PreviewHostCommandRouter(() => session);

        await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.StartCommand,
                "7",
                CreatePayload(CreateStartRequest("xaml-1"))),
            CancellationToken.None);

        AxsgPreviewHostResponseEnvelope response = await router.HandleCommandAsync(
            new AxsgPreviewHostCommandEnvelope(
                AxsgPreviewHostProtocol.InputCommand,
                "8",
                CreatePayload(new AxsgPreviewHostInputRequest
                {
                    EventType = "key",
                    IsDown = true,
                    Key = "a",
                    Code = "KeyA",
                    Location = 0,
                    KeySymbol = "a",
                    Modifiers = new AxsgPreviewHostInputModifiers(
                        Alt: false,
                        Control: true,
                        Shift: false,
                        Meta: false)
                })),
            CancellationToken.None);

        Assert.True(response.Ok);
        Assert.NotNull(session.LastInputRequest);
        Assert.Equal("key", session.LastInputRequest!.EventType);
        Assert.Equal("KeyA", session.LastInputRequest.Code);
        Assert.True(session.LastInputRequest.Modifiers!.Control);
    }

    private static AxsgPreviewHostStartRequest CreateStartRequest(string xamlText)
    {
        return new AxsgPreviewHostStartRequest(
            "dotnet",
            "artifacts/host.dll",
            "artifacts/previewer.dll",
            "artifacts/host.runtimeconfig.json",
            "artifacts/host.deps.json",
            "artifacts/source.dll",
            null,
            "/Pages/MainView.axaml",
            xamlText,
            "sourceGenerated",
            800,
            600,
            2);
    }

    private static JsonElement CreatePayload<T>(T value)
    {
        return JsonSerializer.SerializeToDocument(value, JsonRpcSerializer.DefaultOptions).RootElement.Clone();
    }

    private sealed class FakePreviewHostSession : IPreviewHostSession
    {
        public event Action<string>? Log;

        public event Action<string>? PreviewUrlPublished;

        public event Action<AxsgPreviewHostUpdateResultEventPayload>? UpdateCompleted;

        public event Action<int?>? HostExited;

        public int DisposeCount { get; private set; }

        public AxsgPreviewHostStartRequest? StartedRequest { get; private set; }

        public string? LastHotReloadXaml { get; private set; }

        public AxsgPreviewHostInputRequest? LastInputRequest { get; private set; }

        public Task<AxsgPreviewHostStartResponse> StartAsync(
            AxsgPreviewHostStartRequest request,
            CancellationToken cancellationToken)
        {
            StartedRequest = request;
            return Task.FromResult(new AxsgPreviewHostStartResponse(
                "http://127.0.0.1:7000",
                1234,
                7000,
                Guid.Parse("11111111-1111-1111-1111-111111111111")));
        }

        public Task UpdateAsync(string xamlText, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<AxsgPreviewHostHotReloadResponse> HotReloadAsync(
            string xamlText,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastHotReloadXaml = xamlText;
            var result = new AxsgPreviewHostHotReloadResponse(
                true,
                null,
                null,
                DateTimeOffset.UtcNow);
            UpdateCompleted?.Invoke(new AxsgPreviewHostUpdateResultEventPayload(true, null, null));
            return Task.FromResult(result);
        }

        public Task SendInputAsync(AxsgPreviewHostInputRequest request, CancellationToken cancellationToken)
        {
            LastInputRequest = request;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public void RaiseLog(string message)
        {
            Log?.Invoke(message);
        }

        public void RaisePreviewStarted(string previewUrl)
        {
            PreviewUrlPublished?.Invoke(previewUrl);
        }

        public void RaiseUpdateCompleted(AxsgPreviewHostUpdateResultEventPayload result)
        {
            UpdateCompleted?.Invoke(result);
        }

        public void RaiseHostExited(int? exitCode)
        {
            HostExited?.Invoke(exitCode);
        }
    }
}
