using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class XamlSourceGenHotReloadEventBus : IXamlSourceGenHotReloadEventBus
{
    public static XamlSourceGenHotReloadEventBus Instance { get; } = new();

    private XamlSourceGenHotReloadEventBus()
    {
    }

    public event Action<Type[]?>? HotReloaded;

    public event Action<Type, Exception>? HotReloadFailed;

    public event Action<Type, Exception>? HotReloadRudeEditDetected;

    public event Action<Type, string, Exception>? HotReloadHandlerFailed;

    public event Action<SourceGenHotReloadUpdateContext>? HotReloadPipelineStarted;

    public event Action<SourceGenHotReloadUpdateContext>? HotReloadPipelineCompleted;

    public event Action<SourceGenHotReloadTransportStatus>? HotReloadTransportStatusChanged;

    public event Action<SourceGenHotReloadRemoteOperationStatus>? HotReloadRemoteOperationStatusChanged;

    public void PublishHotReloaded(Type[]? types)
    {
        HotReloaded?.Invoke(types);
    }

    public void PublishHotReloadFailed(Type type, Exception exception)
    {
        HotReloadFailed?.Invoke(type, exception);
    }

    public void PublishHotReloadRudeEditDetected(Type type, Exception exception)
    {
        HotReloadRudeEditDetected?.Invoke(type, exception);
    }

    public void PublishHotReloadHandlerFailed(Type type, string phase, Exception exception)
    {
        HotReloadHandlerFailed?.Invoke(type, phase, exception);
    }

    public void PublishPipelineStarted(SourceGenHotReloadUpdateContext context)
    {
        HotReloadPipelineStarted?.Invoke(context);
    }

    public void PublishPipelineCompleted(SourceGenHotReloadUpdateContext context)
    {
        HotReloadPipelineCompleted?.Invoke(context);
    }

    public void PublishTransportStatusChanged(SourceGenHotReloadTransportStatus status)
    {
        HotReloadTransportStatusChanged?.Invoke(status);
    }

    public void PublishRemoteOperationStatusChanged(SourceGenHotReloadRemoteOperationStatus status)
    {
        HotReloadRemoteOperationStatusChanged?.Invoke(status);
    }
}
