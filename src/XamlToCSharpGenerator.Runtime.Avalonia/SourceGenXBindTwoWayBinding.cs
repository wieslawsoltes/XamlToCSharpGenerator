using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;

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
    private readonly int _delay;
    private readonly UpdateSourceTrigger _updateSourceTrigger;
    private object? _pendingValue;
    private bool _hasPendingValue;
    private int _delayVersion;
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
        Type? bindBackValueType,
        int delay,
        UpdateSourceTrigger updateSourceTrigger)
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
        _delay = Math.Max(0, delay);
        _updateSourceTrigger = updateSourceTrigger == UpdateSourceTrigger.Default
            ? UpdateSourceTrigger.PropertyChanged
            : updateSourceTrigger;

        if (_updateSourceTrigger == UpdateSourceTrigger.LostFocus &&
            _target is InputElement inputElement)
        {
            inputElement.LostFocus += OnTargetLostFocus;
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(object? value)
    {
        if (Volatile.Read(ref _isApplying) != 0)
        {
            return;
        }

        try
        {
            var convertedValue = ConvertBack(value);

            switch (_updateSourceTrigger)
            {
                case UpdateSourceTrigger.Explicit:
                    StorePendingValue(convertedValue);
                    break;
                case UpdateSourceTrigger.LostFocus:
                    StorePendingValue(convertedValue);
                    break;
                case UpdateSourceTrigger.PropertyChanged when _delay > 0:
                    StorePendingValue(convertedValue);
                    ScheduleDelayedFlush();
                    break;
                default:
                    ApplyBindBack(convertedValue);
                    break;
            }
        }
        catch
        {
            // Keep bind-back failures non-fatal to match binding engine resilience.
        }
    }

    private void OnTargetLostFocus(object? sender, EventArgs e)
    {
        FlushPendingValue();
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

    private void StorePendingValue(object? value)
    {
        _pendingValue = value;
        _hasPendingValue = true;
    }

    private void ScheduleDelayedFlush()
    {
        var version = Interlocked.Increment(ref _delayVersion);
        _ = FlushPendingValueAsync(version);
    }

    private async Task FlushPendingValueAsync(int version)
    {
        try
        {
            await Task.Delay(_delay).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != Volatile.Read(ref _delayVersion))
            {
                return;
            }

            FlushPendingValue();
        });
    }

    private void FlushPendingValue()
    {
        if (!_hasPendingValue)
        {
            return;
        }

        var pendingValue = _pendingValue;
        _pendingValue = null;
        _hasPendingValue = false;
        ApplyBindBack(pendingValue);
    }

    private void ApplyBindBack(object? value)
    {
        if (Interlocked.Exchange(ref _isApplying, 1) != 0)
        {
            return;
        }

        try
        {
            if (TryResolveSource(out var source))
            {
                _bindBack(source, value);
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
