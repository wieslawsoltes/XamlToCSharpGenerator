using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.RemoteProtocol.Studio;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides transport-neutral studio remote-design command handling shared by TCP and future adapters.
/// </summary>
internal sealed class AxsgStudioRemoteCommandRouter
{
    private readonly AxsgRuntimeQueryService _runtimeQueryService;
    private readonly AxsgRuntimeHotDesignService _hotDesignService;
    private readonly AxsgRuntimeStudioService _studioService;

    public AxsgStudioRemoteCommandRouter()
        : this(new AxsgRuntimeQueryService())
    {
    }

    internal AxsgStudioRemoteCommandRouter(
        AxsgRuntimeQueryService runtimeQueryService,
        AxsgRuntimeHotDesignService? hotDesignService = null,
        AxsgRuntimeStudioService? studioService = null)
    {
        _runtimeQueryService = runtimeQueryService ?? throw new ArgumentNullException(nameof(runtimeQueryService));
        _hotDesignService = hotDesignService ?? new AxsgRuntimeHotDesignService(_runtimeQueryService);
        _studioService = studioService ?? new AxsgRuntimeStudioService(_runtimeQueryService);
    }

    public async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.Command)
        {
            case AxsgStudioRemoteProtocol.PingCommand:
                return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                    request.Command,
                    request.RequestId,
                    new AxsgStudioPingResponse(true, DateTimeOffset.UtcNow));

            case AxsgStudioRemoteProtocol.GetStatusCommand:
                return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                    request.Command,
                    request.RequestId,
                    AxsgRuntimePayloadBuilder.BuildStudioStatusPayload(_runtimeQueryService.GetStudioStatus()));

            case AxsgStudioRemoteProtocol.GetWorkspaceCommand:
                return HandleGetWorkspace(request);

            case AxsgStudioRemoteProtocol.SelectDocumentCommand:
                return HandleSelectDocument(request);

            case AxsgStudioRemoteProtocol.SelectElementCommand:
                return HandleSelectElement(request);

            case AxsgStudioRemoteProtocol.ApplyDocumentTextCommand:
                return await HandleApplyDocumentTextAsync(request, cancellationToken).ConfigureAwait(false);

            default:
                return AxsgStudioRemoteProtocol.CreateFailureResponse(
                    request.Command,
                    request.RequestId,
                    "Unsupported command '" + request.Command + "'.");
        }
    }

    private AxsgStudioRemoteResponseEnvelope HandleGetWorkspace(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioWorkspaceQueryRequest payload = AxsgStudioRemoteProtocol.ParseWorkspaceQueryRequest(request.Payload);
        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(payload.BuildUri, payload.Search);
        return AxsgStudioRemoteProtocol.CreateSuccessResponse(
            request.Command,
            request.RequestId,
            AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                workspace,
                _runtimeQueryService.GetStudioStatus(),
                _runtimeQueryService.GetHotDesignHitTestMode()));
    }

    private AxsgStudioRemoteResponseEnvelope HandleSelectDocument(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioSelectDocumentRequest payload = AxsgStudioRemoteProtocol.ParseSelectDocumentRequest(request.Payload);
        try
        {
            SourceGenHotDesignWorkspaceSnapshot workspace = _hotDesignService.SelectDocument(payload.BuildUri);
            return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                request.Command,
                request.RequestId,
                AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                    workspace,
                    _runtimeQueryService.GetStudioStatus(),
                    _runtimeQueryService.GetHotDesignHitTestMode()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private AxsgStudioRemoteResponseEnvelope HandleSelectElement(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioSelectElementRequest payload = AxsgStudioRemoteProtocol.ParseSelectElementRequest(request.Payload);
        try
        {
            SourceGenHotDesignWorkspaceSnapshot workspace = _hotDesignService.SelectElement(payload.BuildUri, payload.ElementId);
            return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                request.Command,
                request.RequestId,
                AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                    workspace,
                    _runtimeQueryService.GetStudioStatus(),
                    _runtimeQueryService.GetHotDesignHitTestMode()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleApplyDocumentTextAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioApplyDocumentTextRequest payload = AxsgStudioRemoteProtocol.ParseApplyDocumentTextRequest(request.Payload);
        SourceGenStudioUpdateResult applyResult;
        try
        {
            applyResult = await _studioService
                .ApplyDocumentTextAsync(
                    payload.BuildUri,
                    payload.XamlText,
                    request.RequestId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }

        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(applyResult.BuildUri, search: null);

        return new AxsgStudioRemoteResponseEnvelope(
            Ok: applyResult.Succeeded,
            Command: request.Command,
            RequestId: request.RequestId,
            Error: applyResult.Succeeded ? null : applyResult.Message,
            Payload: new
            {
                applyResult = AxsgRuntimePayloadBuilder.BuildStudioUpdateResultPayload(applyResult),
                workspace = AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                    workspace,
                    _runtimeQueryService.GetStudioStatus(),
                    _runtimeQueryService.GetHotDesignHitTestMode())
            });
    }
}
