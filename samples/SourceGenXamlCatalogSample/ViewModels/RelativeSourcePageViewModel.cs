namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class RelativeSourcePageViewModel : ViewModelBase
{
    private string _query = "Type here";

    public string Query
    {
        get => _query;
        set => SetProperty(ref _query, value);
    }
}
