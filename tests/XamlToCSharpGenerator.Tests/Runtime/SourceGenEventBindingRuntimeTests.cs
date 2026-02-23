using System;
using System.Windows.Input;
using Avalonia.Controls;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenEventBindingRuntimeTests
{
    [Fact]
    public void InvokeCommand_Executes_Root_Command_For_Dot_Path()
    {
        var command = new RecordingCommand();

        SourceGenEventBindingRuntime.InvokeCommand(
            rootObject: command,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.Root,
            commandPath: ".",
            parameterPath: null,
            parameterValue: "payload",
            hasParameterValue: true,
            passEventArgs: false);

        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal("payload", command.LastParameter);
    }

    [Fact]
    public void InvokeCommand_Uses_DataContext_When_Source_Mode_Is_DataContext()
    {
        var command = new RecordingCommand();
        var sender = new Button
        {
            DataContext = command
        };

        SourceGenEventBindingRuntime.InvokeCommand(
            rootObject: new object(),
            sender: sender,
            eventArgs: new EventArgs(),
            SourceGenEventBindingSourceMode.DataContext,
            commandPath: ".",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: true);

        Assert.Equal(1, command.ExecuteCount);
        Assert.IsType<EventArgs>(command.LastParameter);
    }

    [Fact]
    public void InvokeCommand_Ignores_Non_Dot_Path_In_Compatibility_Mode()
    {
        var command = new RecordingCommand();

        SourceGenEventBindingRuntime.InvokeCommand(
            rootObject: command,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.Root,
            commandPath: "SaveCommand",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: false);

        Assert.Equal(0, command.ExecuteCount);
    }

    [Fact]
    public void InvokeMethod_Does_Not_Throw_In_Compatibility_Mode()
    {
        var exception = Record.Exception(() =>
            SourceGenEventBindingRuntime.InvokeMethod(
                rootObject: new object(),
                sender: new object(),
                eventArgs: new EventArgs(),
                SourceGenEventBindingSourceMode.DataContextThenRoot,
                methodPath: "Save",
                parameterPath: "SelectedItem",
                parameterValue: null,
                hasParameterValue: false,
                passEventArgs: true));

        Assert.Null(exception);
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecuteCount { get; private set; }

        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
