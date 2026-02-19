using SourceGenXamlCatalogSample.Infrastructure;
using System.Collections.ObjectModel;
using System.Linq;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class BindingsPageViewModel : ViewModelBase
{
    private CatalogPersonViewModel? _selectedPerson;

    public BindingsPageViewModel()
    {
        People =
        [
            new CatalogPersonViewModel("Ava Green", "ava.green@example.com", new CatalogAddressViewModel("Warsaw")),
            new CatalogPersonViewModel("Marek Novak", "marek.novak@example.com", new CatalogAddressViewModel("Prague")),
            new CatalogPersonViewModel("Sofia Turner", "sofia.turner@example.com", new CatalogAddressViewModel("London"))
        ];

        SelectedPerson = People[0];
        CycleSelectionCommand = new RelayCommand(CycleSelection);
    }

    public ObservableCollection<CatalogPersonViewModel> People { get; }

    public RelayCommand CycleSelectionCommand { get; }

    public CatalogPersonViewModel? FirstPerson => People.Count > 0 ? People[0] : null;

    public CatalogPersonViewModel[] PeopleArray => People.ToArray();

    public string SelectedPersonDisplayName => SelectedPerson is null
        ? "Selected: <none>"
        : SelectedPerson.GetDisplayName("Selected");

    public CatalogPersonViewModel? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (SetProperty(ref _selectedPerson, value))
            {
                OnPropertyChanged(nameof(SelectedPersonDisplayName));
            }
        }
    }

    public string HeaderPrefix { get; } = "Selected";

    private void CycleSelection()
    {
        if (People.Count == 0)
        {
            return;
        }

        if (SelectedPerson is null)
        {
            SelectedPerson = People[0];
            return;
        }

        var index = People.IndexOf(SelectedPerson);
        if (index < 0)
        {
            SelectedPerson = People[0];
            return;
        }

        SelectedPerson = People[(index + 1) % People.Count];
    }
}

public sealed class CatalogPersonViewModel
{
    public CatalogPersonViewModel(string name, string email, CatalogAddressViewModel address)
    {
        Name = name;
        Email = email;
        Address = address;
    }

    public string Name { get; }

    public string Email { get; }

    public CatalogAddressViewModel Address { get; }

    public string GetDisplayName(string prefix)
    {
        return prefix + ": " + Name;
    }
}

public sealed class CatalogAddressViewModel
{
    public CatalogAddressViewModel(string city)
    {
        City = city;
    }

    public string City { get; }
}
