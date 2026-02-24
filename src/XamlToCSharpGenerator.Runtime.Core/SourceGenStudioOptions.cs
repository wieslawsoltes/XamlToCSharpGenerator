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

    public int MaxOperationHistoryEntries { get; set; } = 200;

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
            MaxOperationHistoryEntries = MaxOperationHistoryEntries
        };
    }
}
