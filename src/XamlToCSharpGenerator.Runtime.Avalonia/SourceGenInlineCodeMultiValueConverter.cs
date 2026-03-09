using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class SourceGenInlineCodeMultiValueConverter<TSource, TRoot, TTarget> : IMultiValueConverter
    where TSource : class
    where TRoot : class
    where TTarget : class
{
    private readonly Func<TSource, TRoot, TTarget, object?> _evaluator;
    private readonly object _rootObject;
    private readonly object _targetObject;

    public SourceGenInlineCodeMultiValueConverter(
        Func<TSource, TRoot, TTarget, object?> evaluator,
        object rootObject,
        object targetObject)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _rootObject = rootObject ?? throw new ArgumentNullException(nameof(rootObject));
        _targetObject = targetObject ?? throw new ArgumentNullException(nameof(targetObject));
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 ||
            values[0] is not TSource typedSource ||
            _rootObject is not TRoot typedRoot ||
            _targetObject is not TTarget typedTarget)
        {
            return null;
        }

        try
        {
            return SourceGenExpressionMultiValueConverter<TSource>.CoerceEvaluatedValue(
                _evaluator(typedSource, typedRoot, typedTarget),
                targetType,
                culture);
        }
        catch
        {
            return null;
        }
    }
}
