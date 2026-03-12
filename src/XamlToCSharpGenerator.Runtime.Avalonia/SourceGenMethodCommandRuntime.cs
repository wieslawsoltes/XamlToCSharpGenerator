using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Utilities;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides runtime helpers for source-generated method-to-command bindings.
/// </summary>
public static class SourceGenMethodCommandRuntime
{
    /// <summary>
    /// Creates an <see cref="ICommand"/> wrapper for a bound method target.
    /// </summary>
    public static ICommand? Create(
        object? target,
        Action<object, object?> execute,
        Func<object, object?, bool>? canExecute,
        IReadOnlyList<string>? dependsOnProperties)
    {
        if (target is null)
        {
            return null;
        }

        return new MethodCommand(target, execute, canExecute, dependsOnProperties);
    }

    /// <summary>
    /// Converts a command parameter to the execute-method parameter type using Avalonia conversion rules.
    /// </summary>
    public static T ConvertParameter<T>(object? parameter)
    {
        if (parameter is T typed)
        {
            return typed;
        }

        TypeUtilities.TryConvert(typeof(T), parameter, CultureInfo.CurrentCulture, out var converted);
        return converted is T convertedTyped
            ? convertedTyped
            : (T)converted!;
    }

    private sealed class MethodCommand : ICommand
    {
        private readonly WeakReference<object?> _target;
        private readonly Action<object, object?> _execute;
        private readonly Func<object, object?, bool>? _canExecute;
        private readonly HashSet<string>? _dependsOnProperties;

        public MethodCommand(
            object target,
            Action<object, object?> execute,
            Func<object, object?, bool>? canExecute,
            IReadOnlyList<string>? dependsOnProperties)
        {
            _target = new WeakReference<object?>(target);
            _execute = execute;
            _canExecute = canExecute;

            if (target is INotifyPropertyChanged notifyingTarget &&
                dependsOnProperties is not null &&
                dependsOnProperties.Count > 0)
            {
                _dependsOnProperties = new HashSet<string>(dependsOnProperties, StringComparer.Ordinal);
                AttachPropertyChangedHandler(notifyingTarget);
            }
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (!_target.TryGetTarget(out var target) || target is null)
            {
                return false;
            }

            return _canExecute is null || _canExecute(target, parameter);
        }

        public void Execute(object? parameter)
        {
            if (_target.TryGetTarget(out var target) && target is not null)
            {
                _execute(target, parameter);
            }
        }

        private void AttachPropertyChangedHandler(INotifyPropertyChanged notifyingTarget)
        {
            var weakCommand = new WeakReference<MethodCommand>(this);
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (weakCommand.TryGetTarget(out var command))
                {
                    command.OnTargetPropertyChanged(e);
                    return;
                }

                notifyingTarget.PropertyChanged -= handler;
            };

            notifyingTarget.PropertyChanged += handler;
        }

        private void OnTargetPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_dependsOnProperties is null ||
                string.IsNullOrWhiteSpace(e.PropertyName) ||
                _dependsOnProperties.Contains(e.PropertyName))
            {
                if (!SourceGenDispatcherRuntime.TryPost(
                        () => CanExecuteChanged?.Invoke(this, EventArgs.Empty),
                        DispatcherPriority.Input))
                {
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
