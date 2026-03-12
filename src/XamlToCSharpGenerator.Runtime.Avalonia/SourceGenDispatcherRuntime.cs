using System;
using Avalonia.Threading;

namespace XamlToCSharpGenerator.Runtime;

internal static class SourceGenDispatcherRuntime
{
    private static volatile bool _platformSetupCompleted;

    internal static void MarkPlatformSetupCompleted()
    {
        _platformSetupCompleted = true;
    }

    internal static void ResetForTests()
    {
        _platformSetupCompleted = false;
    }

    internal static bool IsPlatformSetupCompleted => _platformSetupCompleted;

    internal static bool HasControlledUiDispatcher()
    {
        return _platformSetupCompleted;
    }

    internal static bool TryPost(Action action, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!HasControlledUiDispatcher())
        {
            return false;
        }

        Dispatcher.UIThread.Post(action, priority);
        return true;
    }

    internal static bool TryInvoke(Action action, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!HasControlledUiDispatcher())
        {
            return false;
        }

        var dispatcher = Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
        {
            action();
            return true;
        }

        dispatcher.InvokeAsync(action, priority).GetAwaiter().GetResult();
        return true;
    }
}
