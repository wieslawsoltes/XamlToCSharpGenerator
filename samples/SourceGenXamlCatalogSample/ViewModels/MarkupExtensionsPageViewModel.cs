namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class MarkupExtensionsPageViewModel : ViewModelBase
{
    public string GreetingFromViewModel { get; } = "Binding fallback stays available alongside markup extensions.";
}
