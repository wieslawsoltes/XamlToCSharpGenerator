using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class SourceGenExpressionMultiValueConverter<TSource> : IMultiValueConverter where TSource : class
{
    private readonly Func<TSource, object?> _evaluator;

    public SourceGenExpressionMultiValueConverter(Func<TSource, object?> evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0)
        {
            return null;
        }

        try
        {
            if (values[0] is TSource typedSource)
            {
                return CoerceEvaluatedValue(_evaluator(typedSource), targetType, culture);
            }
        }
        catch
        {
            // Keep converter failures non-fatal to match binding error resilience behavior.
        }

        return null;
    }

    private static object? CoerceEvaluatedValue(object? value, Type targetType, CultureInfo culture)
    {
        if (value is null ||
            targetType == typeof(object))
        {
            return value;
        }

        var effectiveTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveTargetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (effectiveTargetType == typeof(string))
        {
            return System.Convert.ToString(value, culture);
        }

        if (effectiveTargetType.IsEnum && value is string enumText)
        {
            try
            {
                return Enum.Parse(effectiveTargetType, enumText, ignoreCase: true);
            }
            catch
            {
                return value;
            }
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(effectiveTargetType))
        {
            try
            {
                return System.Convert.ChangeType(value, effectiveTargetType, culture);
            }
            catch
            {
                return value;
            }
        }

        return value;
    }
}
