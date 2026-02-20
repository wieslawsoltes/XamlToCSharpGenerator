using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotReloadPolicyHandler<TElement, TState> : ISourceGenHotReloadHandler
    where TElement : class
{
    private readonly Func<Type, TElement, bool>? _canHandle;
    private readonly Func<Type, TElement, TState?>? _captureState;
    private readonly Action<Type, TElement, TState?>? _beforeElementReload;
    private readonly Action<Type, TElement, TState?>? _afterElementReload;

    public SourceGenHotReloadPolicyHandler(
        int priority = 0,
        Func<Type, TElement, bool>? canHandle = null,
        Func<Type, TElement, TState?>? captureState = null,
        Action<Type, TElement, TState?>? beforeElementReload = null,
        Action<Type, TElement, TState?>? afterElementReload = null)
    {
        Priority = priority;
        _canHandle = canHandle;
        _captureState = captureState;
        _beforeElementReload = beforeElementReload;
        _afterElementReload = afterElementReload;
    }

    public int Priority { get; }

    public bool CanHandle(Type reloadType, object instance)
    {
        if (instance is not TElement typedInstance)
        {
            return false;
        }

        return _canHandle?.Invoke(reloadType, typedInstance) ?? true;
    }

    public object? CaptureState(Type reloadType, object instance)
    {
        if (instance is not TElement typedInstance || _captureState is null)
        {
            return null;
        }

        return _captureState(reloadType, typedInstance);
    }

    public void BeforeElementReload(Type reloadType, object instance, object? state)
    {
        if (instance is not TElement typedInstance || _beforeElementReload is null)
        {
            return;
        }

        _beforeElementReload(reloadType, typedInstance, CoerceState(state));
    }

    public void AfterElementReload(Type reloadType, object instance, object? state)
    {
        if (instance is not TElement typedInstance || _afterElementReload is null)
        {
            return;
        }

        _afterElementReload(reloadType, typedInstance, CoerceState(state));
    }

    private static TState? CoerceState(object? state)
    {
        return state is TState typedState ? typedState : default;
    }
}

public static class SourceGenHotReloadPolicies
{
    public static SourceGenHotReloadPolicyHandler<TElement, TState> Create<TElement, TState>(
        int priority = 0,
        Func<Type, TElement, bool>? canHandle = null,
        Func<Type, TElement, TState?>? captureState = null,
        Action<Type, TElement, TState?>? beforeElementReload = null,
        Action<Type, TElement, TState?>? afterElementReload = null)
        where TElement : class
    {
        return new SourceGenHotReloadPolicyHandler<TElement, TState>(
            priority,
            canHandle,
            captureState,
            beforeElementReload,
            afterElementReload);
    }
}
