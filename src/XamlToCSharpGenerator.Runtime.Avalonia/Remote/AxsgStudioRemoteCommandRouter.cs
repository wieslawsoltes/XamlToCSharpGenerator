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

            case AxsgStudioRemoteProtocol.GetLogicalTreeCommand:
                return HandleGetLiveTree(request, SourceGenHotDesignHitTestMode.Logical);

            case AxsgStudioRemoteProtocol.GetVisualTreeCommand:
                return HandleGetLiveTree(request, SourceGenHotDesignHitTestMode.Visual);

            case AxsgStudioRemoteProtocol.GetOverlayCommand:
                return HandleGetOverlay(request);

            case AxsgStudioRemoteProtocol.SelectAtPointCommand:
                return HandleSelectAtPoint(request);

            case AxsgStudioRemoteProtocol.ApplyPropertyUpdateCommand:
                return await HandleApplyPropertyUpdateAsync(request, cancellationToken).ConfigureAwait(false);

            case AxsgStudioRemoteProtocol.InsertElementCommand:
                return await HandleInsertElementAsync(request, cancellationToken).ConfigureAwait(false);

            case AxsgStudioRemoteProtocol.RemoveElementCommand:
                return await HandleRemoveElementAsync(request, cancellationToken).ConfigureAwait(false);

            case AxsgStudioRemoteProtocol.UndoCommand:
                return await HandleUndoAsync(request, cancellationToken).ConfigureAwait(false);

            case AxsgStudioRemoteProtocol.RedoCommand:
                return await HandleRedoAsync(request, cancellationToken).ConfigureAwait(false);

            case AxsgStudioRemoteProtocol.SetWorkspaceModeCommand:
                return HandleSetWorkspaceMode(request);

            case AxsgStudioRemoteProtocol.SetHitTestModeCommand:
                return HandleSetHitTestMode(request);

            case AxsgStudioRemoteProtocol.SetPropertyFilterModeCommand:
                return HandleSetPropertyFilterMode(request);

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

    private AxsgStudioRemoteResponseEnvelope HandleGetLiveTree(
        AxsgStudioRemoteRequestEnvelope request,
        SourceGenHotDesignHitTestMode mode)
    {
        AxsgStudioLiveTreeQueryRequest payload = AxsgStudioRemoteProtocol.ParseLiveTreeQueryRequest(request.Payload);
        SourceGenHotDesignLiveTreeSnapshot tree = mode == SourceGenHotDesignHitTestMode.Logical
            ? _runtimeQueryService.GetHotDesignLogicalTree(payload.BuildUri, payload.Search)
            : _runtimeQueryService.GetHotDesignVisualTree(payload.BuildUri, payload.Search);
        return AxsgStudioRemoteProtocol.CreateSuccessResponse(
            request.Command,
            request.RequestId,
            AxsgRuntimePayloadBuilder.BuildHotDesignLiveTreePayload(tree));
    }

    private AxsgStudioRemoteResponseEnvelope HandleGetOverlay(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioBuildUriRequest payload = AxsgStudioRemoteProtocol.ParseBuildUriRequest(request.Payload);
        SourceGenHotDesignOverlaySnapshot overlay = _runtimeQueryService.GetHotDesignOverlay(payload.BuildUri);
        return AxsgStudioRemoteProtocol.CreateSuccessResponse(
            request.Command,
            request.RequestId,
            AxsgRuntimePayloadBuilder.BuildHotDesignOverlayPayload(overlay));
    }

    private AxsgStudioRemoteResponseEnvelope HandleSelectAtPoint(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioSelectAtPointRequest payload = AxsgStudioRemoteProtocol.ParseSelectAtPointRequest(request.Payload);
        if (!payload.X.HasValue || !payload.Y.HasValue)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "x and y are required.");
        }

        try
        {
            SourceGenHotDesignHitTestMode? mode = ParseOptionalEnum<SourceGenHotDesignHitTestMode>(payload.HitTestMode);
            SourceGenHotDesignHitTestResult result = _hotDesignService.SelectAtPoint(
                payload.X.Value,
                payload.Y.Value,
                payload.BuildUri,
                payload.UpdateSelection ?? true,
                mode);
            return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                request.Command,
                request.RequestId,
                AxsgRuntimePayloadBuilder.BuildHotDesignHitTestPayload(result));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleApplyPropertyUpdateAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioApplyPropertyUpdateRequest payload = AxsgStudioRemoteProtocol.ParseApplyPropertyUpdateRequest(request.Payload);
        var updateRequest = new SourceGenHotDesignPropertyUpdateRequest
        {
            BuildUri = payload.BuildUri,
            TargetTypeName = payload.TargetTypeName,
            ElementId = payload.ElementId,
            PropertyName = payload.PropertyName ?? string.Empty,
            PropertyValue = payload.PropertyValue,
            RemoveProperty = payload.RemoveProperty ?? false,
            PersistChangesToSource = payload.PersistChangesToSource,
            WaitForHotReload = payload.WaitForHotReload,
            FallbackToRuntimeApplyOnTimeout = payload.FallbackToRuntimeApplyOnTimeout
        };

        try
        {
            SourceGenHotDesignApplyResult result = await _hotDesignService
                .ApplyPropertyUpdateAsync(updateRequest, cancellationToken)
                .ConfigureAwait(false);
            return BuildHotDesignMutationResponse(request, result, payload.BuildUri ?? result.BuildUri);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleInsertElementAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioInsertElementRequest payload = AxsgStudioRemoteProtocol.ParseInsertElementRequest(request.Payload);
        var insertRequest = new SourceGenHotDesignElementInsertRequest
        {
            BuildUri = payload.BuildUri,
            TargetTypeName = payload.TargetTypeName,
            ParentElementId = payload.ParentElementId,
            ElementName = payload.ElementName ?? string.Empty,
            XamlFragment = payload.XamlFragment,
            PersistChangesToSource = payload.PersistChangesToSource,
            WaitForHotReload = payload.WaitForHotReload,
            FallbackToRuntimeApplyOnTimeout = payload.FallbackToRuntimeApplyOnTimeout
        };

        try
        {
            SourceGenHotDesignApplyResult result = await _hotDesignService
                .InsertElementAsync(insertRequest, cancellationToken)
                .ConfigureAwait(false);
            return BuildHotDesignMutationResponse(request, result, payload.BuildUri ?? result.BuildUri);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleRemoveElementAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioRemoveElementRequest payload = AxsgStudioRemoteProtocol.ParseRemoveElementRequest(request.Payload);
        var removeRequest = new SourceGenHotDesignElementRemoveRequest
        {
            BuildUri = payload.BuildUri,
            TargetTypeName = payload.TargetTypeName,
            ElementId = payload.ElementId ?? string.Empty,
            PersistChangesToSource = payload.PersistChangesToSource,
            WaitForHotReload = payload.WaitForHotReload,
            FallbackToRuntimeApplyOnTimeout = payload.FallbackToRuntimeApplyOnTimeout
        };

        try
        {
            SourceGenHotDesignApplyResult result = await _hotDesignService
                .RemoveElementAsync(removeRequest, cancellationToken)
                .ConfigureAwait(false);
            return BuildHotDesignMutationResponse(request, result, payload.BuildUri ?? result.BuildUri);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleUndoAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioBuildUriRequest payload = AxsgStudioRemoteProtocol.ParseBuildUriRequest(request.Payload);
        try
        {
            SourceGenHotDesignApplyResult result = await _hotDesignService.UndoAsync(payload.BuildUri, cancellationToken).ConfigureAwait(false);
            return BuildHotDesignMutationResponse(request, result, payload.BuildUri ?? result.BuildUri);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleRedoAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioBuildUriRequest payload = AxsgStudioRemoteProtocol.ParseBuildUriRequest(request.Payload);
        try
        {
            SourceGenHotDesignApplyResult result = await _hotDesignService.RedoAsync(payload.BuildUri, cancellationToken).ConfigureAwait(false);
            return BuildHotDesignMutationResponse(request, result, payload.BuildUri ?? result.BuildUri);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                ex.Message);
        }
    }

    private AxsgStudioRemoteResponseEnvelope HandleSetWorkspaceMode(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioModeRequest payload = AxsgStudioRemoteProtocol.ParseModeRequest(request.Payload);
        try
        {
            _hotDesignService.SetWorkspaceMode(ParseRequiredEnum<SourceGenHotDesignWorkspaceMode>(payload.Mode, "mode"));
            return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                request.Command,
                request.RequestId,
                BuildWorkspacePayload(_runtimeQueryService.GetHotDesignWorkspace().ActiveBuildUri));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(request.Command, request.RequestId, ex.Message);
        }
    }

    private AxsgStudioRemoteResponseEnvelope HandleSetHitTestMode(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioModeRequest payload = AxsgStudioRemoteProtocol.ParseModeRequest(request.Payload);
        try
        {
            _hotDesignService.SetHitTestMode(ParseRequiredEnum<SourceGenHotDesignHitTestMode>(payload.Mode, "mode"));
            return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                request.Command,
                request.RequestId,
                BuildWorkspacePayload(_runtimeQueryService.GetHotDesignWorkspace().ActiveBuildUri));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(request.Command, request.RequestId, ex.Message);
        }
    }

    private AxsgStudioRemoteResponseEnvelope HandleSetPropertyFilterMode(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioModeRequest payload = AxsgStudioRemoteProtocol.ParseModeRequest(request.Payload);
        try
        {
            _hotDesignService.SetPropertyFilterMode(ParseRequiredEnum<SourceGenHotDesignPropertyFilterMode>(payload.Mode, "mode"));
            return AxsgStudioRemoteProtocol.CreateSuccessResponse(
                request.Command,
                request.RequestId,
                BuildWorkspacePayload(_runtimeQueryService.GetHotDesignWorkspace().ActiveBuildUri));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(request.Command, request.RequestId, ex.Message);
        }
    }

    private object BuildWorkspacePayload(string? buildUri)
    {
        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(buildUri, search: null);
        return AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
            workspace,
            _runtimeQueryService.GetStudioStatus(),
            _runtimeQueryService.GetHotDesignHitTestMode());
    }

    private AxsgStudioRemoteResponseEnvelope BuildHotDesignMutationResponse(
        AxsgStudioRemoteRequestEnvelope request,
        SourceGenHotDesignApplyResult result,
        string? buildUri)
    {
        string? effectiveBuildUri = buildUri ?? result.BuildUri;
        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(effectiveBuildUri, search: null);
        SourceGenHotDesignOverlaySnapshot overlay = _runtimeQueryService.GetHotDesignOverlay(effectiveBuildUri);
        return new AxsgStudioRemoteResponseEnvelope(
            Ok: true,
            Command: request.Command,
            RequestId: request.RequestId,
            Error: null,
            Payload: new
            {
                applyResult = AxsgRuntimePayloadBuilder.BuildHotDesignApplyResultPayload(result),
                workspace = AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                    workspace,
                    _runtimeQueryService.GetStudioStatus(),
                    _runtimeQueryService.GetHotDesignHitTestMode()),
                overlay = AxsgRuntimePayloadBuilder.BuildHotDesignOverlayPayload(overlay)
            });
    }

    private static TEnum ParseRequiredEnum<TEnum>(string? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value, ignoreCase: true, out TEnum parsedValue))
        {
            throw new InvalidOperationException(parameterName + " must be a valid " + typeof(TEnum).Name + " value.");
        }

        return parsedValue;
    }

    private static TEnum? ParseOptionalEnum<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse(value, ignoreCase: true, out TEnum parsedValue)
            ? parsedValue
            : null;
    }
}
