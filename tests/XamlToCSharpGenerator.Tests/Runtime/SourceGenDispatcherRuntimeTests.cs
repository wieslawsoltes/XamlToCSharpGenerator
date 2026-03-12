using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Threading;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class SourceGenDispatcherRuntimeTests
{
    private static readonly FieldInfo UiThreadField =
        typeof(Dispatcher).GetField("s_uiThread", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void TryPost_DoesNotCreate_UiThreadDispatcher_Without_Controlled_Dispatcher()
    {
        var originalUiThread = UiThreadField.GetValue(null);
        var originalPlatformSetupCompleted = SourceGenDispatcherRuntime.IsPlatformSetupCompleted;
        SourceGenDispatcherRuntime.ResetForTests();
        UiThreadField.SetValue(null, null);

        try
        {
            var posted = SourceGenDispatcherRuntime.TryPost(static () => { }, DispatcherPriority.Background);

            Assert.False(posted);
            Assert.Null(UiThreadField.GetValue(null));
        }
        finally
        {
            if (originalPlatformSetupCompleted)
            {
                SourceGenDispatcherRuntime.MarkPlatformSetupCompleted();
            }

            UiThreadField.SetValue(null, originalUiThread);
        }
    }

    [Fact]
    public void TryInvoke_DoesNotCreate_UiThreadDispatcher_Without_Controlled_Dispatcher()
    {
        var originalUiThread = UiThreadField.GetValue(null);
        var originalPlatformSetupCompleted = SourceGenDispatcherRuntime.IsPlatformSetupCompleted;
        SourceGenDispatcherRuntime.ResetForTests();
        UiThreadField.SetValue(null, null);

        try
        {
            var invoked = SourceGenDispatcherRuntime.TryInvoke(static () => { }, DispatcherPriority.Background);

            Assert.False(invoked);
            Assert.Null(UiThreadField.GetValue(null));
        }
        finally
        {
            if (originalPlatformSetupCompleted)
            {
                SourceGenDispatcherRuntime.MarkPlatformSetupCompleted();
            }

            UiThreadField.SetValue(null, originalUiThread);
        }
    }

    [Fact]
    public void MethodCommandRuntime_DoesNotCreate_UiThreadDispatcher_When_CanExecuteChanges_Before_Dispatcher_Startup()
    {
        var originalUiThread = UiThreadField.GetValue(null);
        var originalPlatformSetupCompleted = SourceGenDispatcherRuntime.IsPlatformSetupCompleted;
        SourceGenDispatcherRuntime.ResetForTests();
        UiThreadField.SetValue(null, null);

        try
        {
            var target = new DeferredCommandTarget();
            var command = Assert.IsAssignableFrom<ICommand>(
                SourceGenMethodCommandRuntime.Create(
                    target,
                    static (_, _) => { },
                    canExecute: null,
                    dependsOnProperties: ["State"]));
            var canExecuteChangedCount = 0;
            command.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

            target.RaiseStateChanged();

            Assert.Equal(1, canExecuteChangedCount);
            Assert.Null(UiThreadField.GetValue(null));
        }
        finally
        {
            if (originalPlatformSetupCompleted)
            {
                SourceGenDispatcherRuntime.MarkPlatformSetupCompleted();
            }

            UiThreadField.SetValue(null, originalUiThread);
        }
    }

    private sealed class DeferredCommandTarget : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void RaiseStateChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("State"));
        }
    }
}
