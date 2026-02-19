namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class ResourcesIncludesPageViewModel : ViewModelBase
{
    public string Description { get; } = "Resource dictionaries are merged via ResourceInclude. Last include wins for duplicate keys.";
}
