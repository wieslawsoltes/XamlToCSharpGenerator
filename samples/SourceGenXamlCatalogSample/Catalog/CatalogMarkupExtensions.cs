using Avalonia.Markup.Xaml;
using System;

namespace SourceGenXamlCatalogSample.Catalog;

public sealed class CatalogPrefixExtension : MarkupExtension
{
    public CatalogPrefixExtension(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public string Prefix { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Prefix + Value;
    }
}

public static class SampleStaticValues
{
    public static string VersionLabel => "Catalog v1";

    public static string SecondaryLabel => "x:Static resolved";
}
