using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.PreviewerHost;

internal interface IPreviewHostSession : IAsyncDisposable
{
    event Action<string>? Log;

    event Action<string>? PreviewUrlPublished;

    event Action<AxsgPreviewHostUpdateResultEventPayload>? UpdateCompleted;

    event Action<int?>? HostExited;

    Task<AxsgPreviewHostStartResponse> StartAsync(AxsgPreviewHostStartRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(string xamlText, CancellationToken cancellationToken);

    Task<AxsgPreviewHostHotReloadResponse> HotReloadAsync(
        string xamlText,
        TimeSpan? timeout,
        CancellationToken cancellationToken);

    Task SendInputAsync(AxsgPreviewHostInputRequest request, CancellationToken cancellationToken);
}

internal sealed class PreviewHostCommandRouter : IAsyncDisposable
{
    private readonly Func<IPreviewHostSession> _sessionFactory;
    private IPreviewHostSession? _session;

    public PreviewHostCommandRouter()
        : this(() => new PreviewSession())
    {
    }

    internal PreviewHostCommandRouter(Func<IPreviewHostSession> sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public event Action<AxsgPreviewHostEventEnvelope>? EventPublished;

    public async Task<AxsgPreviewHostResponseEnvelope> HandleCommandAsync(
        AxsgPreviewHostCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return command.Command switch
            {
                AxsgPreviewHostProtocol.PingCommand => AxsgPreviewHostProtocol.CreateSuccessResponse(
                    command.RequestId,
                    new AxsgPreviewHostPingResponse(true)),
                AxsgPreviewHostProtocol.StartCommand => await HandleStartAsync(command, cancellationToken).ConfigureAwait(false),
                AxsgPreviewHostProtocol.UpdateCommand => await HandleUpdateAsync(command, cancellationToken).ConfigureAwait(false),
                AxsgPreviewHostProtocol.HotReloadCommand => await HandleHotReloadAsync(command, cancellationToken).ConfigureAwait(false),
                AxsgPreviewHostProtocol.InputCommand => await HandleInputAsync(command, cancellationToken).ConfigureAwait(false),
                AxsgPreviewHostProtocol.StopCommand => await HandleStopAsync(command).ConfigureAwait(false),
                _ => AxsgPreviewHostProtocol.CreateFailureResponse(
                    command.RequestId,
                    "Unsupported command '" + command.Command + "'.")
            };
        }
        catch (Exception ex)
        {
            return AxsgPreviewHostProtocol.CreateFailureResponse(command.RequestId, ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCurrentSessionAsync().ConfigureAwait(false);
    }

    private async Task<AxsgPreviewHostResponseEnvelope> HandleStartAsync(
        AxsgPreviewHostCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        await DisposeCurrentSessionAsync().ConfigureAwait(false);

        IPreviewHostSession session = _sessionFactory();
        AttachSession(session);
        _session = session;

        try
        {
            AxsgPreviewHostStartRequest request = AxsgPreviewHostProtocol.ParseStartRequest(command.Payload);
            AxsgPreviewHostStartResponse result = await session.StartAsync(request, cancellationToken).ConfigureAwait(false);
            return AxsgPreviewHostProtocol.CreateSuccessResponse(command.RequestId, result);
        }
        catch
        {
            await DisposeCurrentSessionAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<AxsgPreviewHostResponseEnvelope> HandleUpdateAsync(
        AxsgPreviewHostCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        IPreviewHostSession session = _session ?? throw new InvalidOperationException("Preview session has not been started.");
        AxsgPreviewHostUpdateRequest request = AxsgPreviewHostProtocol.ParseUpdateRequest(command.Payload);
        await session.UpdateAsync(request.XamlText, cancellationToken).ConfigureAwait(false);
        return AxsgPreviewHostProtocol.CreateSuccessResponse(command.RequestId);
    }

    private async Task<AxsgPreviewHostResponseEnvelope> HandleStopAsync(AxsgPreviewHostCommandEnvelope command)
    {
        await DisposeCurrentSessionAsync().ConfigureAwait(false);
        return AxsgPreviewHostProtocol.CreateSuccessResponse(command.RequestId);
    }

    private async Task<AxsgPreviewHostResponseEnvelope> HandleInputAsync(
        AxsgPreviewHostCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        IPreviewHostSession session = _session ?? throw new InvalidOperationException("Preview session has not been started.");
        AxsgPreviewHostInputRequest request = AxsgPreviewHostProtocol.ParseInputRequest(command.Payload);
        await session.SendInputAsync(request, cancellationToken).ConfigureAwait(false);
        return AxsgPreviewHostProtocol.CreateSuccessResponse(command.RequestId);
    }

    private async Task<AxsgPreviewHostResponseEnvelope> HandleHotReloadAsync(
        AxsgPreviewHostCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        IPreviewHostSession session = _session ?? throw new InvalidOperationException("Preview session has not been started.");
        AxsgPreviewHostHotReloadRequest request = AxsgPreviewHostProtocol.ParseHotReloadRequest(command.Payload);
        TimeSpan? timeout = request.TimeoutMs.HasValue
            ? TimeSpan.FromMilliseconds(request.TimeoutMs.Value)
            : null;
        AxsgPreviewHostHotReloadResponse result = await session.HotReloadAsync(
            request.XamlText,
            timeout,
            cancellationToken).ConfigureAwait(false);
        return AxsgPreviewHostProtocol.CreateSuccessResponse(command.RequestId, result);
    }

    private void AttachSession(IPreviewHostSession session)
    {
        session.Log += HandleSessionLog;
        session.PreviewUrlPublished += HandlePreviewStarted;
        session.UpdateCompleted += HandleUpdateCompleted;
        session.HostExited += HandleHostExited;
    }

    private void DetachSession(IPreviewHostSession session)
    {
        session.Log -= HandleSessionLog;
        session.PreviewUrlPublished -= HandlePreviewStarted;
        session.UpdateCompleted -= HandleUpdateCompleted;
        session.HostExited -= HandleHostExited;
    }

    private async Task DisposeCurrentSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        IPreviewHostSession session = _session;
        _session = null;
        DetachSession(session);
        await session.DisposeAsync().ConfigureAwait(false);
    }

    private void HandleSessionLog(string message)
    {
        EventPublished?.Invoke(
            AxsgPreviewHostProtocol.CreateEvent(
                AxsgPreviewHostProtocol.LogEvent,
                new AxsgPreviewHostLogEventPayload(message)));
    }

    private void HandlePreviewStarted(string previewUrl)
    {
        EventPublished?.Invoke(
            AxsgPreviewHostProtocol.CreateEvent(
                AxsgPreviewHostProtocol.PreviewStartedEvent,
                new AxsgPreviewHostPreviewStartedEventPayload(previewUrl)));
    }

    private void HandleUpdateCompleted(AxsgPreviewHostUpdateResultEventPayload result)
    {
        EventPublished?.Invoke(
            AxsgPreviewHostProtocol.CreateEvent(
                AxsgPreviewHostProtocol.UpdateResultEvent,
                result));
    }

    private void HandleHostExited(int? exitCode)
    {
        EventPublished?.Invoke(
            AxsgPreviewHostProtocol.CreateEvent(
                AxsgPreviewHostProtocol.HostExitedEvent,
                new AxsgPreviewHostHostExitedEventPayload(exitCode)));
    }
}
