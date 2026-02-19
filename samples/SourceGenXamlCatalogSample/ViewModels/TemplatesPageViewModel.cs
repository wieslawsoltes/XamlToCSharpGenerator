using System.Collections.ObjectModel;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class TemplatesPageViewModel : ViewModelBase
{
    public ObservableCollection<CatalogPersonViewModel> People { get; } =
    [
        new CatalogPersonViewModel("Alina", "alina@example.com", new CatalogAddressViewModel("Berlin")),
        new CatalogPersonViewModel("Brian", "brian@example.com", new CatalogAddressViewModel("Madrid")),
        new CatalogPersonViewModel("Carla", "carla@example.com", new CatalogAddressViewModel("Oslo"))
    ];
}
