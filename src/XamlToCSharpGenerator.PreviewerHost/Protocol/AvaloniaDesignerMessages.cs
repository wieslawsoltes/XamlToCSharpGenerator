using System.Collections.Generic;

namespace XamlToCSharpGenerator.PreviewerHost.Protocol;

internal static class AvaloniaDesignerMessageGuids
{
    public static readonly Guid UpdateXaml = new("9AEC9A2E-6315-4066-B4BA-E9A9EFD0F8CC");
    public static readonly Guid UpdateXamlResult = new("B7A70093-0C5D-47FD-9261-22086D43A2E2");
    public static readonly Guid StartDesignerSession = new("854887CF-2694-4EB6-B499-7461B6FB96C7");
    public static readonly Guid HtmlTransportStarted = new("53778004-78FA-4381-8EC3-176A6F2328B6");
}

internal sealed record StartDesignerSessionPayload(string SessionId);

internal sealed record HtmlTransportStartedPayload(string Uri);

internal sealed record PreviewExceptionDetails(
    string? ExceptionType,
    string? Message,
    int? LineNumber,
    int? LinePosition);

internal sealed record UpdateXamlResultPayload(
    string? Error,
    string? Handle,
    PreviewExceptionDetails? Exception);

internal sealed record UnknownDesignerMessage(Guid MessageType, IReadOnlyDictionary<string, object?> Document);
