using System;
using System.Text.Json;

namespace XamlToCSharpGenerator.RemoteProtocol.Preview;

/// <summary>
/// Represents the normalized start request payload for the AXSG preview helper host.
/// </summary>
public sealed record AxsgPreviewHostStartRequest(
    string DotNetCommand,
    string HostAssemblyPath,
    string PreviewerToolPath,
    string RuntimeConfigPath,
    string DepsFilePath,
    string SourceAssemblyPath,
    string? SourceFilePath,
    string XamlFileProjectPath,
    string XamlText,
    string PreviewCompilerMode,
    double? PreviewWidth,
    double? PreviewHeight,
    double? PreviewScale);

/// <summary>
/// Represents the successful start response payload returned by the AXSG preview helper host.
/// </summary>
public sealed record AxsgPreviewHostStartResponse(
    string PreviewUrl,
    int TransportPort,
    int PreviewPort,
    Guid SessionId);

/// <summary>
/// Represents a live XAML update request for an active preview helper session.
/// </summary>
public sealed record AxsgPreviewHostUpdateRequest(string XamlText);

/// <summary>
/// Represents an in-process preview hot reload request for an active preview helper session.
/// </summary>
public sealed record AxsgPreviewHostHotReloadRequest(string XamlText, int? TimeoutMs);

/// <summary>
/// Represents modifier state captured for preview input dispatch.
/// </summary>
public sealed record AxsgPreviewHostInputModifiers(
    bool Alt,
    bool Control,
    bool Shift,
    bool Meta);

/// <summary>
/// Represents a normalized preview input request for an active preview helper session.
/// </summary>
public sealed record AxsgPreviewHostInputRequest
{
    public string? EventType { get; init; }

    public bool? IsDown { get; init; }

    public string? Key { get; init; }

    public string? Code { get; init; }

    public int? Location { get; init; }

    public string? KeySymbol { get; init; }

    public string? Text { get; init; }

    public AxsgPreviewHostInputModifiers? Modifiers { get; init; }
}

/// <summary>
/// Represents a preview design operation request for an active preview helper session.
/// </summary>
public sealed record AxsgPreviewHostDesignRequest(
    string Operation,
    JsonElement Arguments);

/// <summary>
/// Represents the payload returned for a successful preview helper ping.
/// </summary>
public sealed record AxsgPreviewHostPingResponse(bool Pong);

/// <summary>
/// Describes a preview exception reported by the designer transport.
/// </summary>
public sealed record AxsgPreviewHostExceptionDetails(
    string? ExceptionType,
    string? Message,
    int? LineNumber,
    int? LinePosition);

/// <summary>
/// Represents a preview helper log event payload.
/// </summary>
public sealed record AxsgPreviewHostLogEventPayload(string Message);

/// <summary>
/// Represents the event payload raised when the preview URL becomes available.
/// </summary>
public sealed record AxsgPreviewHostPreviewStartedEventPayload(string PreviewUrl);

/// <summary>
/// Represents the event payload raised after a live XAML update completes.
/// </summary>
public sealed record AxsgPreviewHostUpdateResultEventPayload(
    bool Succeeded,
    string? Error,
    AxsgPreviewHostExceptionDetails? Exception);

/// <summary>
/// Represents the completed result of an in-process preview hot reload operation.
/// </summary>
public sealed record AxsgPreviewHostHotReloadResponse(
    bool Succeeded,
    string? Error,
    AxsgPreviewHostExceptionDetails? Exception,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// Represents the event payload raised when the preview host process exits.
/// </summary>
public sealed record AxsgPreviewHostHostExitedEventPayload(int? ExitCode, string? Error);
