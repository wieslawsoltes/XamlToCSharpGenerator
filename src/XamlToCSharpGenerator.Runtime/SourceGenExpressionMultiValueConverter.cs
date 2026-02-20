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
                return _evaluator(typedSource);
            }
        }
        catch
        {
            // Keep converter failures non-fatal to match binding error resilience behavior.
        }

        return null;
    }
}
