using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class SourceGenCompiledBindingStreamHelperTests
{
    [Fact]
    public void UnwrapTask_Returns_Result_For_Completed_Generic_Task()
    {
        var task = Task.FromResult("hello");

        var result = SourceGenCompiledBindingStreamHelper.UnwrapTask(task);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void UnwrapTask_Returns_Default_For_Incomplete_Or_Failed_Generic_Task()
    {
        var incompleteTask = new TaskCompletionSource<int>().Task;
        var faultedTask = Task.FromException<int>(new InvalidOperationException("failure"));
        var canceledTask = Task.FromCanceled<int>(new CancellationToken(canceled: true));

        Assert.Equal(0, SourceGenCompiledBindingStreamHelper.UnwrapTask(incompleteTask));
        Assert.Equal(0, SourceGenCompiledBindingStreamHelper.UnwrapTask(faultedTask));
        Assert.Equal(0, SourceGenCompiledBindingStreamHelper.UnwrapTask(canceledTask));
    }

    [Fact]
    public void UnwrapObservable_Tracks_Last_Published_Value()
    {
        var observable = new TestObservable<string>();

        var firstRead = SourceGenCompiledBindingStreamHelper.UnwrapObservable(observable);
        observable.Publish("first");
        var secondRead = SourceGenCompiledBindingStreamHelper.UnwrapObservable(observable);
        observable.Publish("second");
        var thirdRead = SourceGenCompiledBindingStreamHelper.UnwrapObservable(observable);

        Assert.Null(firstRead);
        Assert.Equal("first", secondRead);
        Assert.Equal("second", thirdRead);
    }

    private sealed class TestObservable<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = new();

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            return new Subscription(_observers, observer);
        }

        public void Publish(T value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly List<IObserver<T>> _observers;
            private readonly IObserver<T> _observer;

            public Subscription(List<IObserver<T>> observers, IObserver<T> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                _observers.Remove(_observer);
            }
        }
    }
}
