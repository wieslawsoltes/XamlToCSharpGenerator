using System;
using System.Text.Json;

namespace XamlToCSharpGenerator.RemoteProtocol.Studio;

/// <summary>
/// Defines the shared AXSG studio remote-design command catalog and lightweight request/response contracts.
/// </summary>
public static class AxsgStudioRemoteProtocol
{
    /// <summary>
    /// The ping command name.
    /// </summary>
    public const string PingCommand = "ping";

    /// <summary>
    /// The studio status query command name.
    /// </summary>
    public const string GetStatusCommand = "getstatus";

    /// <summary>
    /// The hot-design workspace query command name.
    /// </summary>
    public const string GetWorkspaceCommand = "getworkspace";

    /// <summary>
    /// The active document selection command name.
    /// </summary>
    public const string SelectDocumentCommand = "selectdocument";

    /// <summary>
    /// The active element selection command name.
    /// </summary>
    public const string SelectElementCommand = "selectelement";

    /// <summary>
    /// The apply-document-text command name.
    /// </summary>
    public const string ApplyDocumentTextCommand = "applydocumenttext";

    /// <summary>
    /// The logical live-tree query command name.
    /// </summary>
    public const string GetLogicalTreeCommand = "getlogicaltree";

    /// <summary>
    /// The visual live-tree query command name.
    /// </summary>
    public const string GetVisualTreeCommand = "getvisualtree";

    /// <summary>
    /// The overlay snapshot query command name.
    /// </summary>
    public const string GetOverlayCommand = "getoverlay";

    /// <summary>
    /// The point hit-test command name.
    /// </summary>
    public const string SelectAtPointCommand = "selectatpoint";

    /// <summary>
    /// The apply-property-update command name.
    /// </summary>
    public const string ApplyPropertyUpdateCommand = "applypropertyupdate";

    /// <summary>
    /// The insert-element command name.
    /// </summary>
    public const string InsertElementCommand = "insertelement";

    /// <summary>
    /// The remove-element command name.
    /// </summary>
    public const string RemoveElementCommand = "removeelement";

    /// <summary>
    /// The undo command name.
    /// </summary>
    public const string UndoCommand = "undo";

    /// <summary>
    /// The redo command name.
    /// </summary>
    public const string RedoCommand = "redo";

    /// <summary>
    /// The set-workspace-mode command name.
    /// </summary>
    public const string SetWorkspaceModeCommand = "setworkspacemode";

    /// <summary>
    /// The set-hit-test-mode command name.
    /// </summary>
    public const string SetHitTestModeCommand = "sethittestmode";

    /// <summary>
    /// The set-property-filter-mode command name.
    /// </summary>
    public const string SetPropertyFilterModeCommand = "setpropertyfiltermode";

    /// <summary>
    /// Parses a single line-delimited studio remote-design request.
    /// </summary>
    public static AxsgStudioRemoteRequestEnvelope ParseRequestLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Request line is required.", nameof(json));
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Request payload must be a JSON object.");
        }

        string? command = TryGetString(root, "command") ?? TryGetString(root, "messageType");
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new JsonException("Request command is missing.");
        }

        string? requestId = NormalizeOptionalText(TryGetString(root, "requestId"));
        JsonElement payload = root.TryGetProperty("payload", out JsonElement payloadElement)
            ? payloadElement.Clone()
            : root.Clone();
        return new AxsgStudioRemoteRequestEnvelope(command.Trim().ToLowerInvariant(), requestId, payload);
    }

    /// <summary>
    /// Parses a workspace query payload.
    /// </summary>
    public static AxsgStudioWorkspaceQueryRequest ParseWorkspaceQueryRequest(JsonElement payload)
    {
        return new AxsgStudioWorkspaceQueryRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            NormalizeOptionalText(TryGetString(payload, "search")));
    }

    /// <summary>
    /// Parses a select-document payload.
    /// </summary>
    public static AxsgStudioSelectDocumentRequest ParseSelectDocumentRequest(JsonElement payload)
    {
        return new AxsgStudioSelectDocumentRequest(NormalizeOptionalText(TryGetString(payload, "buildUri")));
    }

    /// <summary>
    /// Parses a select-element payload.
    /// </summary>
    public static AxsgStudioSelectElementRequest ParseSelectElementRequest(JsonElement payload)
    {
        return new AxsgStudioSelectElementRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            NormalizeOptionalText(TryGetString(payload, "elementId")));
    }

    /// <summary>
    /// Parses an apply-document-text payload.
    /// </summary>
    public static AxsgStudioApplyDocumentTextRequest ParseApplyDocumentTextRequest(JsonElement payload)
    {
        return new AxsgStudioApplyDocumentTextRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            TryGetString(payload, "xamlText"));
    }

    public static AxsgStudioLiveTreeQueryRequest ParseLiveTreeQueryRequest(JsonElement payload)
    {
        return new AxsgStudioLiveTreeQueryRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            NormalizeOptionalText(TryGetString(payload, "search")));
    }

    public static AxsgStudioSelectAtPointRequest ParseSelectAtPointRequest(JsonElement payload)
    {
        return new AxsgStudioSelectAtPointRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            TryGetDouble(payload, "x"),
            TryGetDouble(payload, "y"),
            TryGetBoolean(payload, "updateSelection"),
            NormalizeOptionalText(TryGetString(payload, "hitTestMode")));
    }

    public static AxsgStudioApplyPropertyUpdateRequest ParseApplyPropertyUpdateRequest(JsonElement payload)
    {
        return new AxsgStudioApplyPropertyUpdateRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            NormalizeOptionalText(TryGetString(payload, "targetTypeName")),
            NormalizeOptionalText(TryGetString(payload, "elementId")),
            NormalizeOptionalText(TryGetString(payload, "propertyName")),
            NormalizeOptionalText(TryGetString(payload, "propertyValue")),
            TryGetBoolean(payload, "removeProperty"),
            TryGetBoolean(payload, "persistChangesToSource"),
            TryGetBoolean(payload, "waitForHotReload"),
            TryGetBoolean(payload, "fallbackToRuntimeApplyOnTimeout"));
    }

    public static AxsgStudioInsertElementRequest ParseInsertElementRequest(JsonElement payload)
    {
        return new AxsgStudioInsertElementRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            NormalizeOptionalText(TryGetString(payload, "targetTypeName")),
            NormalizeOptionalText(TryGetString(payload, "parentElementId")),
            NormalizeOptionalText(TryGetString(payload, "elementName")),
            NormalizeOptionalText(TryGetString(payload, "xamlFragment")),
            TryGetBoolean(payload, "persistChangesToSource"),
            TryGetBoolean(payload, "waitForHotReload"),
            TryGetBoolean(payload, "fallbackToRuntimeApplyOnTimeout"));
    }

    public static AxsgStudioRemoveElementRequest ParseRemoveElementRequest(JsonElement payload)
    {
        return new AxsgStudioRemoveElementRequest(
            NormalizeOptionalText(TryGetString(payload, "buildUri")),
            NormalizeOptionalText(TryGetString(payload, "targetTypeName")),
            NormalizeOptionalText(TryGetString(payload, "elementId")),
            TryGetBoolean(payload, "persistChangesToSource"),
            TryGetBoolean(payload, "waitForHotReload"),
            TryGetBoolean(payload, "fallbackToRuntimeApplyOnTimeout"));
    }

    public static AxsgStudioBuildUriRequest ParseBuildUriRequest(JsonElement payload)
    {
        return new AxsgStudioBuildUriRequest(NormalizeOptionalText(TryGetString(payload, "buildUri")));
    }

    public static AxsgStudioModeRequest ParseModeRequest(JsonElement payload, string propertyName = "mode")
    {
        return new AxsgStudioModeRequest(NormalizeOptionalText(TryGetString(payload, propertyName)));
    }

    /// <summary>
    /// Creates a successful studio remote response envelope.
    /// </summary>
    public static AxsgStudioRemoteResponseEnvelope CreateSuccessResponse(
        string command,
        string? requestId,
        object? payload = null)
    {
        return new AxsgStudioRemoteResponseEnvelope(
            Ok: true,
            Command: command,
            RequestId: requestId,
            Error: null,
            Payload: payload);
    }

    /// <summary>
    /// Creates a failed studio remote response envelope.
    /// </summary>
    public static AxsgStudioRemoteResponseEnvelope CreateFailureResponse(
        string command,
        string? requestId,
        string error)
    {
        return new AxsgStudioRemoteResponseEnvelope(
            Ok: false,
            Command: command,
            RequestId: requestId,
            Error: error,
            Payload: null);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out double numberValue) => numberValue,
            _ => null
        };
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

/// <summary>
/// Represents a parsed studio remote-design request.
/// </summary>
public sealed record AxsgStudioRemoteRequestEnvelope(string Command, string? RequestId, JsonElement Payload);

/// <summary>
/// Represents a studio remote-design response envelope.
/// </summary>
public sealed record AxsgStudioRemoteResponseEnvelope(
    bool Ok,
    string Command,
    string? RequestId,
    string? Error,
    object? Payload);

/// <summary>
/// Represents a ping response payload.
/// </summary>
public sealed record AxsgStudioPingResponse(bool Pong, DateTimeOffset UtcNow);

/// <summary>
/// Represents a workspace query request payload.
/// </summary>
public sealed record AxsgStudioWorkspaceQueryRequest(string? BuildUri, string? Search);

/// <summary>
/// Represents a select-document request payload.
/// </summary>
public sealed record AxsgStudioSelectDocumentRequest(string? BuildUri);

/// <summary>
/// Represents a select-element request payload.
/// </summary>
public sealed record AxsgStudioSelectElementRequest(string? BuildUri, string? ElementId);

/// <summary>
/// Represents an apply-document-text request payload.
/// </summary>
public sealed record AxsgStudioApplyDocumentTextRequest(string? BuildUri, string? XamlText);

/// <summary>
/// Represents a live-tree query request payload.
/// </summary>
public sealed record AxsgStudioLiveTreeQueryRequest(string? BuildUri, string? Search);

/// <summary>
/// Represents a point hit-test request payload.
/// </summary>
public sealed record AxsgStudioSelectAtPointRequest(
    string? BuildUri,
    double? X,
    double? Y,
    bool? UpdateSelection,
    string? HitTestMode);

/// <summary>
/// Represents an apply-property-update request payload.
/// </summary>
public sealed record AxsgStudioApplyPropertyUpdateRequest(
    string? BuildUri,
    string? TargetTypeName,
    string? ElementId,
    string? PropertyName,
    string? PropertyValue,
    bool? RemoveProperty,
    bool? PersistChangesToSource,
    bool? WaitForHotReload,
    bool? FallbackToRuntimeApplyOnTimeout);

/// <summary>
/// Represents an insert-element request payload.
/// </summary>
public sealed record AxsgStudioInsertElementRequest(
    string? BuildUri,
    string? TargetTypeName,
    string? ParentElementId,
    string? ElementName,
    string? XamlFragment,
    bool? PersistChangesToSource,
    bool? WaitForHotReload,
    bool? FallbackToRuntimeApplyOnTimeout);

/// <summary>
/// Represents a remove-element request payload.
/// </summary>
public sealed record AxsgStudioRemoveElementRequest(
    string? BuildUri,
    string? TargetTypeName,
    string? ElementId,
    bool? PersistChangesToSource,
    bool? WaitForHotReload,
    bool? FallbackToRuntimeApplyOnTimeout);

/// <summary>
/// Represents a build-uri-only request payload.
/// </summary>
public sealed record AxsgStudioBuildUriRequest(string? BuildUri);

/// <summary>
/// Represents a mode mutation request payload.
/// </summary>
public sealed record AxsgStudioModeRequest(string? Mode);
