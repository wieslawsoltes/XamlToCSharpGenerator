using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides helpers for source-generated compiled-binding stream operators.
/// </summary>
public static class SourceGenCompiledBindingStreamHelper
{
    private static readonly ConditionalWeakTable<object, object> ObservableStates = new();

    /// <summary>
    /// Unwraps a task stream value for generated compiled-binding accessors.
    /// </summary>
    public static T? UnwrapTask<T>(Task<T>? task)
    {
        if (task is null)
        {
            return default;
        }

        if (!task.IsCompleted || task.IsFaulted || task.IsCanceled)
        {
            return default;
        }

        return task.Result;
    }

    /// <summary>
    /// Unwraps a non-generic task stream value for generated compiled-binding accessors.
    /// </summary>
    public static object? UnwrapTask(Task? task)
    {
        if (task is null)
        {
            return null;
        }

        if (!task.IsCompleted || task.IsFaulted || task.IsCanceled)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Unwraps an observable stream value for generated compiled-binding accessors.
    /// </summary>
    public static T? UnwrapObservable<T>(IObservable<T>? observable)
    {
        if (observable is null)
        {
            return default;
        }

        var state = ObservableStates.GetValue(
            observable,
            static key =>
            {
                var typedObservable = (IObservable<T>)key;
                var observer = new ObservableObserver<T>();
                var subscription = typedObservable.Subscribe(observer);
                return new ObservableSubscriptionState<T>(observer, subscription);
            });

        if (state is ObservableSubscriptionState<T> typedState)
        {
            return typedState.Observer.LastValue;
        }

        return default;
    }

    private sealed class ObservableObserver<T> : IObserver<T>
    {
        public T? LastValue { get; private set; }

        public void OnNext(T value)
        {
            LastValue = value;
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

    private sealed class ObservableSubscriptionState<T>
    {
        public ObservableSubscriptionState(ObservableObserver<T> observer, IDisposable subscription)
        {
            Observer = observer;
            Subscription = subscription;
        }

        public ObservableObserver<T> Observer { get; }

        public IDisposable Subscription { get; }
    }
}
