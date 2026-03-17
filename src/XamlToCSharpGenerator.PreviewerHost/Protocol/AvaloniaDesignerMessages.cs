using System.Collections.Generic;

namespace XamlToCSharpGenerator.PreviewerHost.Protocol;

internal static class AvaloniaDesignerMessageGuids
{
    public static readonly Guid UpdateXaml = new("9AEC9A2E-6315-4066-B4BA-E9A9EFD0F8CC");
    public static readonly Guid UpdateXamlResult = new("B7A70093-0C5D-47FD-9261-22086D43A2E2");
    public static readonly Guid StartDesignerSession = new("854887CF-2694-4EB6-B499-7461B6FB96C7");
    public static readonly Guid HtmlTransportStarted = new("53778004-78FA-4381-8EC3-176A6F2328B6");
    public static readonly Guid ClientViewportAllocated = new("BD7A8DE6-3DB8-4A13-8583-D6D4AB189A31");
    public static readonly Guid ClientRenderInfo = new("7A3C25D3-3652-438D-8EF1-86E942CC96C0");
    public static readonly Guid KeyEvent = new("1C3B691E-3D54-4237-BFB0-9FEA83BC1DB8");
    public static readonly Guid TextInputEvent = new("C174102E-7405-4594-916F-B10B8248A17D");
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
