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

    public AxsgStudioRemoteCommandRouter()
        : this(new AxsgRuntimeQueryService())
    {
    }

    internal AxsgStudioRemoteCommandRouter(AxsgRuntimeQueryService runtimeQueryService)
    {
        _runtimeQueryService = runtimeQueryService ?? throw new ArgumentNullException(nameof(runtimeQueryService));
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
            AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(workspace, _runtimeQueryService.GetStudioStatus()));
    }

    private AxsgStudioRemoteResponseEnvelope HandleSelectDocument(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioSelectDocumentRequest payload = AxsgStudioRemoteProtocol.ParseSelectDocumentRequest(request.Payload);
        if (string.IsNullOrWhiteSpace(payload.BuildUri))
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "buildUri is required.");
        }

        bool documentExists = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .Any(candidate => string.Equals(candidate.BuildUri, payload.BuildUri, StringComparison.OrdinalIgnoreCase));
        if (!documentExists)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "No registered document matches buildUri '" + payload.BuildUri + "'.");
        }

        XamlSourceGenHotDesignTool.SelectDocument(payload.BuildUri);
        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(payload.BuildUri, search: null);
        return AxsgStudioRemoteProtocol.CreateSuccessResponse(
            request.Command,
            request.RequestId,
            AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(workspace, _runtimeQueryService.GetStudioStatus()));
    }

    private AxsgStudioRemoteResponseEnvelope HandleSelectElement(AxsgStudioRemoteRequestEnvelope request)
    {
        AxsgStudioSelectElementRequest payload = AxsgStudioRemoteProtocol.ParseSelectElementRequest(request.Payload);
        if (string.IsNullOrWhiteSpace(payload.ElementId))
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "elementId is required.");
        }

        string? activeBuildUri = payload.BuildUri;
        if (string.IsNullOrWhiteSpace(activeBuildUri))
        {
            activeBuildUri = _runtimeQueryService.GetHotDesignWorkspace().ActiveBuildUri;
        }

        if (string.IsNullOrWhiteSpace(activeBuildUri))
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "No active document is available for element selection.");
        }

        bool documentExists = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .Any(candidate => string.Equals(candidate.BuildUri, activeBuildUri, StringComparison.OrdinalIgnoreCase));
        if (!documentExists)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "No registered document matches buildUri '" + activeBuildUri + "'.");
        }

        SourceGenHotDesignWorkspaceSnapshot workspaceBeforeSelection = _runtimeQueryService.GetHotDesignWorkspace(activeBuildUri, search: null);
        if (!ContainsElementId(workspaceBeforeSelection.Elements, payload.ElementId))
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "No element with id '" + payload.ElementId + "' exists in buildUri '" + activeBuildUri + "'.");
        }

        XamlSourceGenHotDesignTool.SelectElement(activeBuildUri, payload.ElementId);
        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(activeBuildUri, search: null);
        return AxsgStudioRemoteProtocol.CreateSuccessResponse(
            request.Command,
            request.RequestId,
            AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(workspace, _runtimeQueryService.GetStudioStatus()));
    }

    private async ValueTask<AxsgStudioRemoteResponseEnvelope> HandleApplyDocumentTextAsync(
        AxsgStudioRemoteRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        AxsgStudioApplyDocumentTextRequest payload = AxsgStudioRemoteProtocol.ParseApplyDocumentTextRequest(request.Payload);
        string? buildUri = payload.BuildUri;
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            buildUri = _runtimeQueryService.GetHotDesignWorkspace().ActiveBuildUri;
        }

        if (string.IsNullOrWhiteSpace(buildUri))
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "buildUri is required.");
        }

        if (payload.XamlText is null)
        {
            return AxsgStudioRemoteProtocol.CreateFailureResponse(
                request.Command,
                request.RequestId,
                "xamlText is required.");
        }

        SourceGenHotDesignDocumentDescriptor? document = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .FirstOrDefault(candidate => string.Equals(candidate.BuildUri, buildUri, StringComparison.OrdinalIgnoreCase));

        var applyRequest = new SourceGenStudioUpdateRequest
        {
            RequestId = request.RequestId,
            BuildUri = buildUri,
            TargetType = document?.RootType,
            TargetTypeName = document?.RootType.FullName,
            XamlText = payload.XamlText
        };

        SourceGenStudioUpdateResult applyResult = await XamlSourceGenStudioManager
            .ApplyUpdateAsync(applyRequest, cancellationToken)
            .ConfigureAwait(false);
        SourceGenHotDesignWorkspaceSnapshot workspace = _runtimeQueryService.GetHotDesignWorkspace(buildUri, search: null);

        return new AxsgStudioRemoteResponseEnvelope(
            Ok: applyResult.Succeeded,
            Command: request.Command,
            RequestId: request.RequestId,
            Error: applyResult.Succeeded ? null : applyResult.Message,
            Payload: new
            {
                applyResult = AxsgRuntimePayloadBuilder.BuildStudioUpdateResultPayload(applyResult),
                workspace = AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(workspace, _runtimeQueryService.GetStudioStatus())
            });
    }

    private static bool ContainsElementId(IReadOnlyList<SourceGenHotDesignElementNode> elements, string elementId)
    {
        if (elements.Count == 0)
        {
            return false;
        }

        for (int index = 0; index < elements.Count; index++)
        {
            SourceGenHotDesignElementNode element = elements[index];
            if (string.Equals(element.Id, elementId, StringComparison.Ordinal))
            {
                return true;
            }

            if (ContainsElementId(element.Children, elementId))
            {
                return true;
            }
        }

        return false;
    }
}
