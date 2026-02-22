using System;

namespace XamlToCSharpGenerator.Runtime;

public interface ISourceGenHotReloadHandler
{
    int Priority => 0;

    bool CanHandle(Type reloadType, object instance) => true;

    object? CaptureState(Type reloadType, object instance) => null;

    void BeforeVisualTreeUpdate(SourceGenHotReloadUpdateContext context)
    {
    }

    void BeforeElementReload(Type reloadType, object instance, object? state)
    {
    }

    void AfterElementReload(Type reloadType, object instance, object? state)
    {
    }

    void AfterVisualTreeUpdate(SourceGenHotReloadUpdateContext context)
    {
    }

    void ReloadCompleted(SourceGenHotReloadUpdateContext context)
    {
    }
}
