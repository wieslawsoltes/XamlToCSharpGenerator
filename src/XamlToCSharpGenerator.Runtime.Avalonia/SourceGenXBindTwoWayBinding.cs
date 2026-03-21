using System;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Data.Converters;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class SourceGenXBindBindBackObserver<TSource> : IObserver<object?>
{
    private readonly SourceGenBindingDependency _source;
    private readonly Action<TSource, object?> _bindBack;
    private readonly object _rootObject;
    private readonly object _targetObject;
    private readonly AvaloniaObject _target;
    private readonly IValueConverter? _converter;
    private readonly CultureInfo? _converterCulture;
    private readonly object? _converterParameter;
    private readonly Type? _bindBackValueType;
    private int _isApplying;

    public SourceGenXBindBindBackObserver(
        SourceGenBindingDependency source,
        Action<TSource, object?> bindBack,
        object rootObject,
        object targetObject,
        AvaloniaObject target,
        IValueConverter? converter,
        CultureInfo? converterCulture,
        object? converterParameter,
        Type? bindBackValueType)
    {
        _source = source;
        _bindBack = bindBack ?? throw new ArgumentNullException(nameof(bindBack));
        _rootObject = rootObject ?? throw new ArgumentNullException(nameof(rootObject));
        _targetObject = targetObject ?? throw new ArgumentNullException(nameof(targetObject));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _converter = converter;
        _converterCulture = converterCulture;
        _converterParameter = converterParameter;
        _bindBackValueType = bindBackValueType;
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(object? value)
    {
        if (Interlocked.Exchange(ref _isApplying, 1) != 0)
        {
            return;
        }

        try
        {
            if (TryResolveSource(out var source))
            {
                _bindBack(source, ConvertBack(value));
            }
        }
        catch
        {
            // Keep bind-back failures non-fatal to match binding engine resilience.
        }
        finally
        {
            Volatile.Write(ref _isApplying, 0);
        }
    }

    private bool TryResolveSource(out TSource source)
    {
        source = default!;
        if (!SourceGenMarkupExtensionRuntime.TryResolveDependencySource(
                _source,
                _targetObject ?? _target,
                _rootObject,
                out var rawSource))
        {
            return false;
        }

        if (rawSource is TSource typedSource)
        {
            source = typedSource;
            return true;
        }

        return false;
    }

    private object? ConvertBack(object? value)
    {
        if (_converter is null)
        {
            return value;
        }

        try
        {
            return _converter.ConvertBack(
                value,
                _bindBackValueType ?? typeof(object),
                _converterParameter,
                _converterCulture ?? CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }
}
