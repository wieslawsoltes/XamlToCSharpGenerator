using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SourceGenXamlCatalogSample.Pages;

public sealed class XBindContact
{
    public XBindContact(string name, string email, string city, string? notes)
    {
        Name = name;
        Email = email;
        City = city;
        Notes = notes;
    }

    public string Name { get; }

    public string Email { get; }

    public string City { get; }

    public string? Notes { get; }

    public string DisplayName => Name + " <" + Email + ">";

    public override string ToString()
    {
        return "Pathless current item: " + Name + " from " + City;
    }
}

public static class XBindSampleHelpers
{
    public static string Prefix => "Static x:Bind helper:";
}

public sealed class XBindNameCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Converter received <null>";
        }

        var effectiveCulture = culture ?? CultureInfo.InvariantCulture;
        var converted = effectiveCulture.TextInfo.ToUpper(text);
        var suffix = parameter as string;
        return string.IsNullOrWhiteSpace(suffix)
            ? converted
            : converted + " [" + suffix + "]";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
