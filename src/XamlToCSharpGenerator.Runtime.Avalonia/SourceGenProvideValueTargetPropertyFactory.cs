using System;
using global::Avalonia.Data.Core;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenProvideValueTargetPropertyFactory
{
    public static IPropertyInfo CreateWritable<TTarget, TValue>(string name, Action<TTarget, TValue> setter)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (setter is null)
        {
            throw new ArgumentNullException(nameof(setter));
        }

        object? Getter(object _)
        {
            throw new NotSupportedException($"Property '{name}' is write-only in source-generated context.");
        }

        void Setter(object target, object? value)
        {
            if (target is not TTarget typedTarget)
            {
                throw new ArgumentException(
                    $"Target instance must be assignable to '{typeof(TTarget)}' for property '{name}'.",
                    nameof(target));
            }

            if (value is null)
            {
                setter(typedTarget, default!);
                return;
            }

            if (value is TValue typedValue)
            {
                setter(typedTarget, typedValue);
                return;
            }

            setter(typedTarget, (TValue)value);
        }

        return new ClrPropertyInfo(name, Getter, Setter, typeof(TValue));
    }

    public static IPropertyInfo CreateReadOnly<TTarget, TValue>(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        object? Getter(object _)
        {
            throw new NotSupportedException($"Property '{name}' is read-only in source-generated context.");
        }

        return new ClrPropertyInfo(name, Getter, null, typeof(TValue));
    }
}
