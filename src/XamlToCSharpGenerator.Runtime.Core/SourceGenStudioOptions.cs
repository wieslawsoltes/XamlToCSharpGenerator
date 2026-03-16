using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenStudioOptions
{
    public bool PersistChangesToSource { get; set; } = true;

    public SourceGenStudioWaitMode WaitMode { get; set; } = SourceGenStudioWaitMode.WaitForLocalOnly;

    public TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public SourceGenStudioFallbackPolicy FallbackPolicy { get; set; } = SourceGenStudioFallbackPolicy.RuntimeApplyOnTimeout;

    public bool ShowOverlayIndicator { get; set; } = true;

    public bool EnableExternalWindow { get; set; } = true;

    public bool AutoOpenStudioWindowOnStartup { get; set; }

    public bool EnableTracing { get; set; }

    public SourceGenStudioCanvasLayoutMode CanvasLayoutMode { get; set; } = SourceGenStudioCanvasLayoutMode.SideBySide;

    public int MaxOperationHistoryEntries { get; set; } = 200;

    public bool EnableRemoteDesign { get; set; }

    public string RemoteHost { get; set; } = "0.0.0.0";

    public int RemotePort { get; set; } = 45831;

    public string? VncEndpoint { get; set; }

    public bool AutoOpenVncViewerOnDesktop { get; set; }

    public SourceGenStudioOptions Clone()
    {
        return new SourceGenStudioOptions
        {
            PersistChangesToSource = PersistChangesToSource,
            WaitMode = WaitMode,
            UpdateTimeout = UpdateTimeout,
            FallbackPolicy = FallbackPolicy,
            ShowOverlayIndicator = ShowOverlayIndicator,
            EnableExternalWindow = EnableExternalWindow,
            AutoOpenStudioWindowOnStartup = AutoOpenStudioWindowOnStartup,
            EnableTracing = EnableTracing,
            CanvasLayoutMode = CanvasLayoutMode,
            MaxOperationHistoryEntries = MaxOperationHistoryEntries,
            EnableRemoteDesign = EnableRemoteDesign,
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            VncEndpoint = VncEndpoint,
            AutoOpenVncViewerOnDesktop = AutoOpenVncViewerOnDesktop
        };
    }
}
