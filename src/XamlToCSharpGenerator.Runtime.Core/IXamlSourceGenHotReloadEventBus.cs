using System;

namespace XamlToCSharpGenerator.Runtime;

public interface IXamlSourceGenHotReloadEventBus
{
    event Action<Type[]?>? HotReloaded;

    event Action<Type, Exception>? HotReloadFailed;

    event Action<Type, Exception>? HotReloadRudeEditDetected;

    event Action<Type, string, Exception>? HotReloadHandlerFailed;

    event Action<SourceGenHotReloadUpdateContext>? HotReloadPipelineStarted;

    event Action<SourceGenHotReloadUpdateContext>? HotReloadPipelineCompleted;

    void PublishHotReloaded(Type[]? types);

    void PublishHotReloadFailed(Type type, Exception exception);

    void PublishHotReloadRudeEditDetected(Type type, Exception exception);

    void PublishHotReloadHandlerFailed(Type type, string phase, Exception exception);

    void PublishPipelineStarted(SourceGenHotReloadUpdateContext context);

    void PublishPipelineCompleted(SourceGenHotReloadUpdateContext context);
}
