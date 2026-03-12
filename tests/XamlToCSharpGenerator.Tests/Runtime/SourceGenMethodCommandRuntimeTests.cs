using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenMethodCommandRuntimeTests
{
    [AvaloniaFact]
    public void Create_Supports_Parameter_Conversion_And_DependsOn_CanExecute()
    {
        var viewModel = new MethodCommandViewModel();
        var command = SourceGenMethodCommandRuntime.Create(
            viewModel,
            static (target, parameter) => ((MethodCommandViewModel)target).Execute(
                SourceGenMethodCommandRuntime.ConvertParameter<int>(parameter)),
            static (target, parameter) => ((MethodCommandViewModel)target).CanExecute(parameter),
            new[] { nameof(MethodCommandViewModel.IsEnabled) });

        Assert.NotNull(command);
        Assert.False(command!.CanExecute("5"));

        var canExecuteChangedCount = 0;
        command.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        viewModel.IsEnabled = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(command.CanExecute("5"));

        command.Execute("5");

        Assert.Equal(5, viewModel.LastValue);
        Assert.Equal(1, canExecuteChangedCount);
    }

    [Fact]
    public void ConvertParameter_Throws_For_Null_NonNullable_ValueType()
    {
        Assert.Throws<NullReferenceException>(() => SourceGenMethodCommandRuntime.ConvertParameter<int>(null));
    }

    [AvaloniaFact]
    public async Task Create_Marshals_CanExecuteChanged_Through_Captured_Avalonia_Context_When_PlatformMarker_Is_Unset()
    {
        var originalPlatformSetupCompleted = SourceGenDispatcherRuntime.IsPlatformSetupCompleted;
        SourceGenDispatcherRuntime.ResetForTests();
        AvaloniaSynchronizationContext.InstallIfNeeded();

        try
        {
            var viewModel = new MethodCommandViewModel();
            var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(
                SourceGenMethodCommandRuntime.Create(
                    viewModel,
                    static (_, _) => { },
                    static (_, _) => true,
                    new[] { nameof(MethodCommandViewModel.IsEnabled) }));

            var raisedOnUiContext = false;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var uiContext = SynchronizationContext.Current;

            command.CanExecuteChanged += (_, _) =>
            {
                raisedOnUiContext = ReferenceEquals(SynchronizationContext.Current, uiContext);
                completion.TrySetResult(true);
            };

            await Task.Run(() => viewModel.IsEnabled = true);
            Dispatcher.UIThread.RunJobs();
            await completion.Task;

            Assert.True(raisedOnUiContext);
        }
        finally
        {
            if (originalPlatformSetupCompleted)
            {
                SourceGenDispatcherRuntime.MarkPlatformSetupCompleted();
            }
        }
    }

    [Fact]
    public void Create_Detaches_PropertyChanged_Handler_When_Command_Is_Collected()
    {
        var target = new CountingNotifyTarget();
        var commandReference = CreateCollectedCommandReference(target);

        Assert.Equal(1, target.SubscriberCount);

        CollectUntil(() => !commandReference.IsAlive && target.SubscriberCount == 0);

        Assert.False(commandReference.IsAlive);
        Assert.Equal(0, target.SubscriberCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateCollectedCommandReference(CountingNotifyTarget target)
    {
        var command = SourceGenMethodCommandRuntime.Create(
            target,
            static (_, _) => { },
            static (_, _) => true,
            new[] { nameof(CountingNotifyTarget.IsEnabled) });

        Assert.NotNull(command);
        return new WeakReference(command);
    }

    private static void CollectUntil(Func<bool> condition)
    {
        for (var iteration = 0; iteration < 10; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (condition())
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.True(condition());
    }

    private sealed class MethodCommandViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }

        public int LastValue { get; private set; }

        public void Execute(int value)
        {
            LastValue = value;
        }

        public bool CanExecute(object? parameter)
        {
            return IsEnabled && parameter is not null;
        }
    }

    private sealed class CountingNotifyTarget : INotifyPropertyChanged
    {
        private readonly List<PropertyChangedEventHandler> _handlers = new();

        public int SubscriberCount => _handlers.Count;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                if (value is not null)
                {
                    _handlers.Add(value);
                }
            }
            remove
            {
                if (value is not null)
                {
                    _handlers.Remove(value);
                }
            }
        }

        public bool IsEnabled { get; set; }
    }
}
