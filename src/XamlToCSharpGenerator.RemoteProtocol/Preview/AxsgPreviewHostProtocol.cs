using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.RemoteProtocol.Preview;

/// <summary>
/// Defines stable AXSG preview helper command and event names shared by the helper transport and adapters.
/// </summary>
public static class AxsgPreviewHostProtocol
{
    /// <summary>
    /// The ping command name.
    /// </summary>
    public const string PingCommand = "ping";

    /// <summary>
    /// The preview start command name.
    /// </summary>
    public const string StartCommand = "start";

    /// <summary>
    /// The live preview update command name.
    /// </summary>
    public const string UpdateCommand = "update";

    /// <summary>
    /// The in-process preview hot reload command name.
    /// </summary>
    public const string HotReloadCommand = "hotReload";

    /// <summary>
    /// The preview stop command name.
    /// </summary>
    public const string StopCommand = "stop";

    /// <summary>
    /// The preview host log event name.
    /// </summary>
    public const string LogEvent = "log";

    /// <summary>
    /// The preview started event name.
    /// </summary>
    public const string PreviewStartedEvent = "previewStarted";

    /// <summary>
    /// The update result event name.
    /// </summary>
    public const string UpdateResultEvent = "updateResult";

    /// <summary>
    /// The host exited event name.
    /// </summary>
    public const string HostExitedEvent = "hostExited";

    /// <summary>
    /// Parses a JSON-line command envelope emitted by the VS Code preview client.
    /// </summary>
    public static AxsgPreviewHostCommandEnvelope ParseCommandLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new ArgumentException("Command line is required.", nameof(line));
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Command payload must be a JSON object.");
        }

        string? commandName = root.TryGetProperty("command", out JsonElement commandElement) &&
                              commandElement.ValueKind == JsonValueKind.String
            ? commandElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new InvalidOperationException("Command name is required.");
        }

        string? requestId = root.TryGetProperty("requestId", out JsonElement requestIdElement) &&
                            requestIdElement.ValueKind == JsonValueKind.String
            ? requestIdElement.GetString()
            : null;
        JsonElement payload = root.TryGetProperty("payload", out JsonElement payloadElement)
            ? payloadElement.Clone()
            : default;
        return new AxsgPreviewHostCommandEnvelope(NormalizeCommandName(commandName), requestId, payload);
    }

    /// <summary>
    /// Parses and normalizes a preview start request payload.
    /// </summary>
    public static AxsgPreviewHostStartRequest ParseStartRequest(JsonElement payload)
    {
        EnsureObjectPayload(payload, "Start payload");
        AxsgPreviewHostStartRequest? request = payload.Deserialize<AxsgPreviewHostStartRequest>(JsonRpcSerializer.DefaultOptions);
        if (request is null)
        {
            throw new InvalidOperationException("Start payload is invalid.");
        }

        string hostAssemblyPath = GetRequiredString(request.HostAssemblyPath, "hostAssemblyPath");
        string previewerToolPath = GetRequiredString(request.PreviewerToolPath, "previewerToolPath");
        string sourceAssemblyPath = GetRequiredString(request.SourceAssemblyPath, "sourceAssemblyPath");
        string xamlFileProjectPath = GetRequiredString(request.XamlFileProjectPath, "xamlFileProjectPath");
        string xamlText = GetRequiredValue(request.XamlText, "xamlText");
        string dotNetCommand = string.IsNullOrWhiteSpace(request.DotNetCommand) ? "dotnet" : request.DotNetCommand;
        string runtimeConfigPath = string.IsNullOrWhiteSpace(request.RuntimeConfigPath)
            ? Path.ChangeExtension(hostAssemblyPath, ".runtimeconfig.json")!
            : request.RuntimeConfigPath;
        string depsFilePath = string.IsNullOrWhiteSpace(request.DepsFilePath)
            ? Path.ChangeExtension(hostAssemblyPath, ".deps.json")!
            : request.DepsFilePath;
        string previewCompilerMode = string.IsNullOrWhiteSpace(request.PreviewCompilerMode)
            ? "avalonia"
            : request.PreviewCompilerMode;

        return request with
        {
            DotNetCommand = dotNetCommand,
            HostAssemblyPath = Path.GetFullPath(hostAssemblyPath),
            PreviewerToolPath = Path.GetFullPath(previewerToolPath),
            RuntimeConfigPath = Path.GetFullPath(runtimeConfigPath),
            DepsFilePath = Path.GetFullPath(depsFilePath),
            SourceAssemblyPath = Path.GetFullPath(sourceAssemblyPath),
            SourceFilePath = string.IsNullOrWhiteSpace(request.SourceFilePath) ? null : Path.GetFullPath(request.SourceFilePath),
            XamlFileProjectPath = xamlFileProjectPath,
            XamlText = xamlText,
            PreviewCompilerMode = previewCompilerMode
        };
    }

    /// <summary>
    /// Parses and validates a preview live-update request payload.
    /// </summary>
    public static AxsgPreviewHostUpdateRequest ParseUpdateRequest(JsonElement payload)
    {
        EnsureObjectPayload(payload, "Update payload");
        AxsgPreviewHostUpdateRequest? request = payload.Deserialize<AxsgPreviewHostUpdateRequest>(JsonRpcSerializer.DefaultOptions);
        if (request is null || request.XamlText is null)
        {
            throw new InvalidOperationException("xamlText is required.");
        }

        return request;
    }

    /// <summary>
    /// Parses and validates a preview in-process hot reload request payload.
    /// </summary>
    public static AxsgPreviewHostHotReloadRequest ParseHotReloadRequest(JsonElement payload)
    {
        EnsureObjectPayload(payload, "Hot reload payload");
        AxsgPreviewHostHotReloadRequest? request = payload.Deserialize<AxsgPreviewHostHotReloadRequest>(JsonRpcSerializer.DefaultOptions);
        if (request is null || request.XamlText is null)
        {
            throw new InvalidOperationException("xamlText is required.");
        }

        if (request.TimeoutMs is < 0)
        {
            throw new InvalidOperationException("timeoutMs must be non-negative.");
        }

        return request;
    }

    /// <summary>
    /// Creates a successful preview helper response envelope.
    /// </summary>
    public static AxsgPreviewHostResponseEnvelope CreateSuccessResponse(string? requestId, object? payload = null)
    {
        return new AxsgPreviewHostResponseEnvelope
        {
            RequestId = requestId,
            Ok = true,
            Payload = JsonRpcNodeHelpers.SerializeResultValue(payload)
        };
    }

    /// <summary>
    /// Creates a failed preview helper response envelope.
    /// </summary>
    public static AxsgPreviewHostResponseEnvelope CreateFailureResponse(string? requestId, string error)
    {
        return new AxsgPreviewHostResponseEnvelope
        {
            RequestId = requestId,
            Ok = false,
            Error = error
        };
    }

    /// <summary>
    /// Creates a preview helper event envelope.
    /// </summary>
    public static AxsgPreviewHostEventEnvelope CreateEvent(string eventName, object? payload)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException("Event name is required.", nameof(eventName));
        }

        return new AxsgPreviewHostEventEnvelope
        {
            Event = eventName,
            Payload = JsonRpcNodeHelpers.SerializeResultValue(payload)
        };
    }

    private static void EnsureObjectPayload(JsonElement payload, string payloadName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(payloadName + " must be a JSON object.");
        }
    }

    private static string NormalizeCommandName(string commandName)
    {
        string normalized = commandName.Trim();

        if (string.Equals(normalized, PingCommand, StringComparison.OrdinalIgnoreCase))
        {
            return PingCommand;
        }

        if (string.Equals(normalized, StartCommand, StringComparison.OrdinalIgnoreCase))
        {
            return StartCommand;
        }

        if (string.Equals(normalized, UpdateCommand, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateCommand;
        }

        if (string.Equals(normalized, HotReloadCommand, StringComparison.OrdinalIgnoreCase))
        {
            return HotReloadCommand;
        }

        if (string.Equals(normalized, StopCommand, StringComparison.OrdinalIgnoreCase))
        {
            return StopCommand;
        }

        return normalized;
    }

    private static string GetRequiredString(string? value, string propertyName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(propertyName + " is required.")
            : value;
    }

    private static string GetRequiredValue(string? value, string propertyName)
    {
        return value ?? throw new InvalidOperationException(propertyName + " is required.");
    }
}

/// <summary>
/// Represents a preview helper command envelope read from the lightweight JSON-line transport.
/// </summary>
public sealed record AxsgPreviewHostCommandEnvelope(string Command, string? RequestId, JsonElement Payload);

/// <summary>
/// Represents a preview helper response envelope written to the lightweight JSON-line transport.
/// </summary>
public sealed record AxsgPreviewHostResponseEnvelope
{
    /// <summary>
    /// Gets the message kind marker.
    /// </summary>
    public string Kind { get; init; } = "response";

    /// <summary>
    /// Gets the client request identifier.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the request succeeded.
    /// </summary>
    public bool Ok { get; init; }

    /// <summary>
    /// Gets the successful response payload, when present.
    /// </summary>
    public JsonNode? Payload { get; init; }

    /// <summary>
    /// Gets the error message for a failed response, when present.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Represents a preview helper event envelope written to the lightweight JSON-line transport.
/// </summary>
public sealed record AxsgPreviewHostEventEnvelope
{
    /// <summary>
    /// Gets the message kind marker.
    /// </summary>
    public string Kind { get; init; } = "event";

    /// <summary>
    /// Gets the event name.
    /// </summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>
    /// Gets the event payload, when present.
    /// </summary>
    public JsonNode? Payload { get; init; }
}
