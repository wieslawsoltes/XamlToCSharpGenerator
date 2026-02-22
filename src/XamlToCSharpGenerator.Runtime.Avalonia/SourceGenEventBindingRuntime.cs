using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Avalonia;

namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenEventBindingSourceMode
{
    DataContextThenRoot = 0,
    DataContext = 1,
    Root = 2
}

public static class SourceGenEventBindingRuntime
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> DataContextPropertyCache = new();

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
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            return;
        }

        try
        {
            if (!TryResolveSource(rootObject, sender, sourceMode, out var source) ||
                source is null)
            {
                return;
            }

            var effectiveSource = source;
            var commandResolved = TryResolvePathValue(effectiveSource, commandPath, out var commandValue) &&
                                  commandValue is ICommand;
            if (!commandResolved &&
                sourceMode == SourceGenEventBindingSourceMode.DataContextThenRoot &&
                !ReferenceEquals(effectiveSource, rootObject))
            {
                effectiveSource = rootObject;
                commandResolved = TryResolvePathValue(effectiveSource, commandPath, out commandValue) &&
                                  commandValue is ICommand;
            }

            if (!commandResolved || commandValue is not ICommand command)
            {
                return;
            }

            var parameter = ResolveParameter(
                effectiveSource,
                eventArgs,
                parameterPath,
                parameterValue,
                hasParameterValue,
                passEventArgs);

            if (!command.CanExecute(parameter))
            {
                return;
            }

            command.Execute(parameter);
        }
        catch
        {
            // Event binding invocation should never crash UI runtime.
        }
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
        if (string.IsNullOrWhiteSpace(methodPath))
        {
            return;
        }

        try
        {
            if (!TryResolveSource(rootObject, sender, sourceMode, out var source) ||
                source is null)
            {
                return;
            }

            var effectiveSource = source;
            var methodResolved = TryResolveMethodTarget(effectiveSource, methodPath, out var methodTarget, out var methodName);
            if (!methodResolved &&
                sourceMode == SourceGenEventBindingSourceMode.DataContextThenRoot &&
                !ReferenceEquals(effectiveSource, rootObject))
            {
                effectiveSource = rootObject;
                methodResolved = TryResolveMethodTarget(effectiveSource, methodPath, out methodTarget, out methodName);
            }

            if (!methodResolved ||
                methodTarget is null ||
                string.IsNullOrWhiteSpace(methodName))
            {
                return;
            }

            var parameter = ResolveParameter(
                effectiveSource,
                eventArgs,
                parameterPath,
                parameterValue,
                hasParameterValue,
                passEventArgs);

            var hasParameter = hasParameterValue || !string.IsNullOrWhiteSpace(parameterPath);
            var invoked = TryInvokeBestMatch(methodTarget, methodName, sender, eventArgs, parameter, hasParameter, passEventArgs);
            if (!invoked &&
                sourceMode == SourceGenEventBindingSourceMode.DataContextThenRoot &&
                !ReferenceEquals(effectiveSource, rootObject) &&
                TryResolveMethodTarget(rootObject, methodPath, out var rootMethodTarget, out var rootMethodName) &&
                rootMethodTarget is not null &&
                !string.IsNullOrWhiteSpace(rootMethodName))
            {
                TryInvokeBestMatch(rootMethodTarget, rootMethodName, sender, eventArgs, parameter, hasParameter, passEventArgs);
            }
        }
        catch
        {
            // Event binding invocation should never crash UI runtime.
        }
    }

    private static bool TryResolveSource(
        object rootObject,
        object? sender,
        SourceGenEventBindingSourceMode sourceMode,
        out object? source)
    {
        source = null;
        switch (sourceMode)
        {
            case SourceGenEventBindingSourceMode.Root:
                source = rootObject;
                return true;
            case SourceGenEventBindingSourceMode.DataContext:
                source = TryGetDataContext(sender) ?? TryGetDataContext(rootObject);
                return source is not null;
            case SourceGenEventBindingSourceMode.DataContextThenRoot:
                source = TryGetDataContext(sender) ?? TryGetDataContext(rootObject) ?? rootObject;
                return source is not null;
            default:
                source = rootObject;
                return true;
        }
    }

    private static object? TryGetDataContext(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IDataContextProvider provider)
        {
            return provider.DataContext;
        }

        if (value is StyledElement styledElement)
        {
            return styledElement.DataContext;
        }

        var property = DataContextPropertyCache.GetOrAdd(
            value.GetType(),
            static type => type.GetProperty("DataContext", InstanceFlags));
        if (property is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryResolvePathValue(object source, string path, out object? value)
    {
        value = source;
        var trimmed = path.Trim();
        if (trimmed.Length == 0 || trimmed == ".")
        {
            return true;
        }

        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (value is null)
            {
                return false;
            }

            var segment = segments[index];
            if (!TryGetMemberValue(value, segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveMethodTarget(object source, string methodPath, out object? target, out string methodName)
    {
        target = source;
        methodName = methodPath.Trim();
        if (methodName.Length == 0)
        {
            return false;
        }

        var lastDot = methodName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= methodName.Length - 1)
        {
            return true;
        }

        var targetPath = methodName.Substring(0, lastDot);
        var candidateMethod = methodName.Substring(lastDot + 1);
        if (candidateMethod.Length == 0)
        {
            return false;
        }

        if (!TryResolvePathValue(source, targetPath, out var resolvedTarget) || resolvedTarget is null)
        {
            return false;
        }

        target = resolvedTarget;
        methodName = candidateMethod;
        return true;
    }

    private static bool TryGetMemberValue(object source, string memberName, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        var type = source.GetType();
        var property = type.GetProperty(memberName, InstanceFlags)
                       ?? type.GetProperty(memberName, InstanceFlags | BindingFlags.IgnoreCase);
        if (property is not null)
        {
            try
            {
                value = property.GetValue(source);
                return true;
            }
            catch
            {
                return false;
            }
        }

        var field = type.GetField(memberName, InstanceFlags)
                    ?? type.GetField(memberName, InstanceFlags | BindingFlags.IgnoreCase);
        if (field is null)
        {
            return false;
        }

        try
        {
            value = field.GetValue(source);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? ResolveParameter(
        object source,
        object? eventArgs,
        string? parameterPath,
        object? parameterValue,
        bool hasParameterValue,
        bool passEventArgs)
    {
        if (!string.IsNullOrWhiteSpace(parameterPath) &&
            TryResolvePathValue(source, parameterPath!, out var resolvedPathParameter))
        {
            return resolvedPathParameter;
        }

        if (hasParameterValue)
        {
            return parameterValue;
        }

        return passEventArgs ? eventArgs : null;
    }

    private static bool TryInvokeBestMatch(
        object target,
        string methodName,
        object? sender,
        object? eventArgs,
        object? parameter,
        bool hasParameter,
        bool passEventArgs)
    {
        var method = ResolveMethod(target, methodName, hasParameter, passEventArgs, sender, eventArgs, parameter, out var arguments);
        if (method is null)
        {
            return false;
        }

        try
        {
            method.Invoke(target, arguments);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo? ResolveMethod(
        object target,
        string methodName,
        bool hasParameter,
        bool passEventArgs,
        object? sender,
        object? eventArgs,
        object? parameter,
        out object?[] arguments)
    {
        arguments = Array.Empty<object?>();
        var methods = target.GetType().GetMethods(InstanceFlags);
        if (methods.Length == 0)
        {
            return null;
        }

        object?[][] candidateArgumentSets;
        if (hasParameter)
        {
            candidateArgumentSets =
            [
                [parameter],
                [sender, parameter],
                [sender, eventArgs, parameter]
            ];
        }
        else if (passEventArgs)
        {
            candidateArgumentSets =
            [
                [sender, eventArgs],
                [eventArgs],
                [sender],
                Array.Empty<object?>()
            ];
        }
        else
        {
            candidateArgumentSets =
            [
                Array.Empty<object?>(),
                [sender],
                [eventArgs],
                [sender, eventArgs]
            ];
        }

        foreach (var candidate in candidateArgumentSets)
        {
            var method = TryFindCompatibleMethod(methods, methodName, candidate, out var convertedArguments);
            if (method is null)
            {
                continue;
            }

            arguments = convertedArguments;
            return method;
        }

        return null;
    }

    private static MethodInfo? TryFindCompatibleMethod(
        MethodInfo[] methods,
        string methodName,
        object?[] candidateArguments,
        out object?[] convertedArguments)
    {
        convertedArguments = Array.Empty<object?>();
        for (var index = 0; index < methods.Length; index++)
        {
            var method = methods[index];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                !string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (method.ReturnType != typeof(void))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != candidateArguments.Length)
            {
                continue;
            }

            var arguments = new object?[candidateArguments.Length];
            var compatible = true;
            for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                var argument = candidateArguments[parameterIndex];
                if (!TryConvertArgument(argument, parameter.ParameterType, out var convertedArgument))
                {
                    compatible = false;
                    break;
                }

                arguments[parameterIndex] = convertedArgument;
            }

            if (!compatible)
            {
                continue;
            }

            convertedArguments = arguments;
            return method;
        }

        return null;
    }

    private static bool TryConvertArgument(object? value, Type targetType, out object? converted)
    {
        if (value is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                converted = null;
                return false;
            }

            converted = null;
            return true;
        }

        if (targetType.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        try
        {
            var destinationType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            converted = Convert.ChangeType(value, destinationType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            converted = null;
            return false;
        }
    }
}
