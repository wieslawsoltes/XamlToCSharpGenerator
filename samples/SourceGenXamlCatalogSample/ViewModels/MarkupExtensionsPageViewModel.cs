namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class MarkupExtensionsPageViewModel : ViewModelBase
{
    public string GreetingFromViewModel { get; } = "Binding fallback stays available alongside markup extensions.";

    public string FirstName { get; } = "Ava";

    public string LastName { get; } = "SourceGen";

    public int Count { get; } = 3;
}
