using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignOptions
{
    public bool PersistChangesToSource { get; set; } = true;

    public bool UseMinimalDiffPersistence { get; set; } = true;

    public bool WaitForHotReload { get; set; } = true;

    public TimeSpan HotReloadWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool FallbackToRuntimeApplyOnTimeout { get; set; }

    public bool EnableTracing { get; set; }

    public int MaxHistoryEntries { get; set; } = 40;

    public SourceGenHotDesignOptions Clone()
    {
        return new SourceGenHotDesignOptions
        {
            PersistChangesToSource = PersistChangesToSource,
            UseMinimalDiffPersistence = UseMinimalDiffPersistence,
            WaitForHotReload = WaitForHotReload,
            HotReloadWaitTimeout = HotReloadWaitTimeout,
            FallbackToRuntimeApplyOnTimeout = FallbackToRuntimeApplyOnTimeout,
            EnableTracing = EnableTracing,
            MaxHistoryEntries = MaxHistoryEntries
        };
    }
}
