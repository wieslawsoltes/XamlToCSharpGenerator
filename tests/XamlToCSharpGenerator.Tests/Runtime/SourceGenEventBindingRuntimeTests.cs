using System;
using System.Windows.Input;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenEventBindingRuntimeTests
{
    [Fact]
    public void InvokeCommand_Executes_Command_From_DataContext()
    {
        var command = new RecordingCommand();
        var viewModel = new EventBindingViewModel
        {
            SaveCommand = command
        };
        var root = new EventBindingRoot
        {
            DataContext = viewModel
        };

        SourceGenEventBindingRuntime.InvokeCommand(
            root,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.DataContextThenRoot,
            commandPath: "SaveCommand",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: false);

        Assert.Equal(1, command.ExecuteCount);
        Assert.Null(command.LastParameter);
    }

    [Fact]
    public void InvokeCommand_Resolves_Parameter_Path_From_DataContext()
    {
        var command = new RecordingCommand();
        var viewModel = new EventBindingViewModel
        {
            SaveCommand = command,
            SelectedItem = "Item-42"
        };
        var root = new EventBindingRoot
        {
            DataContext = viewModel
        };

        SourceGenEventBindingRuntime.InvokeCommand(
            root,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.DataContextThenRoot,
            commandPath: "SaveCommand",
            parameterPath: "SelectedItem",
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: false);

        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal("Item-42", command.LastParameter);
    }

    [Fact]
    public void InvokeMethod_Uses_Root_Source_Mode()
    {
        var root = new EventBindingRoot();

        SourceGenEventBindingRuntime.InvokeMethod(
            root,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.Root,
            methodPath: "RootSave",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: false);

        Assert.Equal(1, root.RootMethodInvocations);
    }

    [Fact]
    public void InvokeMethod_Passes_Event_Args_When_Requested()
    {
        var viewModel = new EventBindingViewModel();
        var root = new EventBindingRoot
        {
            DataContext = viewModel
        };
        var sender = new object();
        var eventArgs = new EventArgs();

        SourceGenEventBindingRuntime.InvokeMethod(
            root,
            sender,
            eventArgs,
            SourceGenEventBindingSourceMode.DataContextThenRoot,
            methodPath: "SaveWithArgs",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: true);

        Assert.Equal(1, viewModel.SaveWithArgsInvocations);
        Assert.Same(sender, viewModel.LastSender);
        Assert.Same(eventArgs, viewModel.LastEventArgs);
    }

    [Fact]
    public void InvokeCommand_Falls_Back_To_Root_When_DataContext_Missing_Command_Path()
    {
        var command = new RecordingCommand();
        var viewModel = new EventBindingViewModel();
        var root = new EventBindingRoot
        {
            DataContext = viewModel,
            RootCommand = command
        };

        SourceGenEventBindingRuntime.InvokeCommand(
            root,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.DataContextThenRoot,
            commandPath: "RootCommand",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: false);

        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public void InvokeMethod_Falls_Back_To_Root_When_DataContext_Missing_Method_Path()
    {
        var root = new EventBindingRoot
        {
            DataContext = new EventBindingViewModel()
        };

        SourceGenEventBindingRuntime.InvokeMethod(
            root,
            sender: null,
            eventArgs: null,
            SourceGenEventBindingSourceMode.DataContextThenRoot,
            methodPath: "RootSave",
            parameterPath: null,
            parameterValue: null,
            hasParameterValue: false,
            passEventArgs: false);

        Assert.Equal(1, root.RootMethodInvocations);
    }

    private sealed class EventBindingRoot
    {
        public object? DataContext { get; set; }

        public ICommand RootCommand { get; set; } = new RecordingCommand();

        public int RootMethodInvocations { get; private set; }

        public void RootSave()
        {
            RootMethodInvocations++;
        }
    }

    private sealed class EventBindingViewModel
    {
        public ICommand SaveCommand { get; set; } = new RecordingCommand();

        public object? SelectedItem { get; set; }

        public int SaveWithArgsInvocations { get; private set; }

        public object? LastSender { get; private set; }

        public object? LastEventArgs { get; private set; }

        public void SaveWithArgs(object? sender, object? args)
        {
            SaveWithArgsInvocations++;
            LastSender = sender;
            LastEventArgs = args;
        }
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
