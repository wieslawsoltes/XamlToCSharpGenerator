using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class SourceGenBindingApplicationHandle : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _bindingExpression;
    private List<Action>? _cleanupActions = new();
    private bool _isDisposed;

    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _isDisposed;
            }
        }
    }

    internal bool HasAttachedBinding
    {
        get
        {
            lock (_gate)
            {
                return _bindingExpression is not null;
            }
        }
    }

    internal bool TrySetBindingExpression(IDisposable? bindingExpression)
    {
        IDisposable? previousBindingExpression = null;
        lock (_gate)
        {
            if (_isDisposed)
            {
                previousBindingExpression = bindingExpression;
            }
            else
            {
                previousBindingExpression = _bindingExpression;
                _bindingExpression = bindingExpression;
                bindingExpression = null;
            }
        }

        previousBindingExpression?.Dispose();
        return bindingExpression is null;
    }

    internal bool TryRegisterCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        var invokeCleanupImmediately = false;
        lock (_gate)
        {
            if (_isDisposed)
            {
                invokeCleanupImmediately = true;
            }
            else
            {
                _cleanupActions ??= new List<Action>();
                _cleanupActions.Add(cleanup);
                return true;
            }
        }

        if (invokeCleanupImmediately)
        {
            cleanup();
        }

        return false;
    }

    public void Dispose()
    {
        IDisposable? bindingExpression;
        List<Action>? cleanupActions;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            bindingExpression = _bindingExpression;
            cleanupActions = _cleanupActions;
            _bindingExpression = null;
            _cleanupActions = null;
        }

        bindingExpression?.Dispose();
        if (cleanupActions is null)
        {
            return;
        }

        foreach (var cleanup in cleanupActions)
        {
            cleanup();
        }
    }
}
