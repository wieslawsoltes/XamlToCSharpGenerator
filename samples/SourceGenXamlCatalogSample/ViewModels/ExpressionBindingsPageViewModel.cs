namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class ExpressionBindingsPageViewModel : ViewModelBase
{
    private int _clickCount;

    public string FirstName { get; } = "Ava";

    public string LastName { get; } = "SourceGen";

    public string ProductName { get; } = "Avalonia Mug";

    public int Count { get; } = 4;

    public int Quantity { get; } = 3;

    public bool IsAdmin { get; } = true;

    public bool IsLoading { get; } = false;

    public bool HasAccount { get; } = true;

    public bool AgreedToTerms { get; } = true;

    public bool IsVip { get; } = true;

    public decimal Price { get; } = 12.5m;

    public decimal TaxRate { get; } = 0.23m;

    public string? Nickname { get; } = null;

    public string[] Tags { get; } = ["xaml", "generator", "avalonia"];

    public int ClickCount
    {
        get => _clickCount;
        set => SetProperty(ref _clickCount, value);
    }

    public string FormatSummary(string first, string last, int count)
    {
        return first + "." + last + " (" + count + ")";
    }
}
