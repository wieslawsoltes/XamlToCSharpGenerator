using System;
using System.Windows.Input;
using Avalonia;

namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenEventBindingSourceMode
{
    DataContextThenRoot = 0,
    DataContext = 1,
    Root = 2
}

/// <summary>
/// Backward-compatible event-binding shim kept for previously generated binaries.
/// New generated code emits fully typed command/method handlers and does not depend on this runtime fallback.
/// </summary>
public static class SourceGenEventBindingRuntime
{
    public static void InvokeCommand(
        object rootObject,
        object? sender,
        object? eventArgs,
        SourceGenEventBindingSourceMode sourceMode,
        string commandPath,
        string? parameterPath,
        object? parameterValue,
        bool hasParameterValue,
        bool passEventArgs)
    {
        // Reflection-free compatibility mode:
        // only supports direct ICommand source (Path="."), preserving no-crash behavior.
        if (!TryResolveSource(rootObject, sender, sourceMode, out var source) ||
            source is null ||
            !string.Equals(commandPath?.Trim(), ".", StringComparison.Ordinal))
        {
            return;
        }

        if (source is not ICommand command)
        {
            return;
        }

        var parameter = hasParameterValue
            ? parameterValue
            : passEventArgs
                ? eventArgs
                : null;
        if (!command.CanExecute(parameter))
        {
            return;
        }

        command.Execute(parameter);
    }

    public static void InvokeMethod(
        object rootObject,
        object? sender,
        object? eventArgs,
        SourceGenEventBindingSourceMode sourceMode,
        string methodPath,
        string? parameterPath,
        object? parameterValue,
        bool hasParameterValue,
        bool passEventArgs)
    {
        // Intentionally no-op in reflection-free compatibility mode.
    }

    private static bool TryResolveSource(
        object rootObject,
        object? sender,
        SourceGenEventBindingSourceMode sourceMode,
        out object? source)
    {
        source = sourceMode switch
        {
            SourceGenEventBindingSourceMode.Root => rootObject,
            SourceGenEventBindingSourceMode.DataContext => TryGetDataContext(sender) ?? TryGetDataContext(rootObject),
            _ => TryGetDataContext(sender) ?? TryGetDataContext(rootObject) ?? rootObject
        };

        return source is not null;
    }

    private static object? TryGetDataContext(object? value)
    {
        return value switch
        {
            IDataContextProvider provider => provider.DataContext,
            _ => null
        };
    }
}
