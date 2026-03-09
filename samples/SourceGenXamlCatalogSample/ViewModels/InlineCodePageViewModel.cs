namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class InlineCodePageViewModel : ViewModelBase
{
    private int _clickCount = 2;
    private string _lastAction = "No inline action yet.";

    public string ProductName { get; } = "SourceGen Bottle";

    public int Quantity { get; } = 4;

    public decimal Price { get; } = 7.5m;

    public bool IsVip { get; } = true;

    public bool IsLoading { get; } = false;

    public bool HasAccount { get; } = true;

    public bool AgreedToTerms { get; } = true;

    public int ClickCount
    {
        get => _clickCount;
        set
        {
            if (SetProperty(ref _clickCount, value))
            {
                LastAction = "Click count updated to " + value + ".";
            }
        }
    }

    public string LastAction
    {
        get => _lastAction;
        set => SetProperty(ref _lastAction, value);
    }

    public string FormatSummary(string productName, int quantity)
    {
        return quantity + " units of " + productName;
    }

    public void RecordSender(object? sender)
    {
        LastAction = "Sender type: " + (sender?.GetType().Name ?? "<null>");
    }
}
