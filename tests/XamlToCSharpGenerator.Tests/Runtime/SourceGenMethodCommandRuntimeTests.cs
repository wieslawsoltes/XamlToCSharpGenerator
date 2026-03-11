using System;
using System.ComponentModel;
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
}
