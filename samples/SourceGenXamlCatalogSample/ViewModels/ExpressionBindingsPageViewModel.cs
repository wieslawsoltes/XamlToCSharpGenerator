namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class ExpressionBindingsPageViewModel : ViewModelBase
{
    public string FirstName { get; } = "Ava";

    public string LastName { get; } = "SourceGen";

    public int Count { get; } = 4;

    public bool IsAdmin { get; } = true;

    public decimal Price { get; } = 12.5m;

    public decimal TaxRate { get; } = 0.23m;

    public string? Nickname { get; } = null;

    public string[] Tags { get; } = ["xaml", "generator", "avalonia"];

    public string FormatSummary(string first, string last, int count)
    {
        return first + "." + last + " (" + count + ")";
    }
}
