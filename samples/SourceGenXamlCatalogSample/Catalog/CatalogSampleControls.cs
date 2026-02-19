using Avalonia;
using Avalonia.Controls;
using System;
using System.Globalization;
using System.Linq;

namespace SourceGenXamlCatalogSample.Catalog;

public sealed class PrimitiveValuesControl : Control
{
    public byte ByteValue { get; set; }

    public decimal DecimalValue { get; set; }

    public TimeSpan SpanValue { get; set; }

    public Uri? UriValue { get; set; }

    public char CharValue { get; set; }

    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        "Byte={0}, Decimal={1}, Span={2}, Uri={3}, Char={4}",
        ByteValue,
        DecimalValue,
        SpanValue,
        UriValue?.ToString() ?? "<null>",
        CharValue);
}

public sealed class PairControl : Control
{
    public PairControl(int left, string right)
    {
        Left = left;
        Right = right;
        LeftRightSummary = left.ToString(CultureInfo.InvariantCulture) + ":" + right;
    }

    public int Left { get; }

    public string Right { get; }

    public string LeftRightSummary { get; }
}

public sealed class GenericFactoryHolder<T> : Control
{
    public GenericFactoryHolder(T value)
    {
        Value = value;
    }

    public T Value { get; }

    public static GenericFactoryHolder<T> Create(T value)
    {
        return new GenericFactoryHolder<T>(value);
    }
}

public sealed class ArrayHostControl : Control
{
    public string[]? Values { get; set; }

    public string ValuesSummary => Values is { Length: > 0 }
        ? string.Join(", ", Values)
        : "<empty>";
}

public sealed class BadgeControl : ContentControl
{
}

public sealed class TemplatedInfoControl : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TemplatedInfoControl, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
