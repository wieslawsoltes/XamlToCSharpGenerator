using System.Collections.ObjectModel;
using SourceGenCrudSample.Infrastructure;

namespace SourceGenCrudSample.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly RelayCommand _addCommand;
    private readonly RelayCommand _updateCommand;
    private readonly RelayCommand _deleteCommand;
    private readonly RelayCommand _clearCommand;

    private PersonRecord? _selectedPerson;
    private string _draftName = string.Empty;
    private string _draftEmail = string.Empty;
    private string _statusMessage = "Select an item to edit, or enter fields and click Add.";
    private int _nextId = 4;

    public MainWindowViewModel()
    {
        People = new ObservableCollection<PersonRecord>
        {
            new(1, "Ava Green", "ava.green@example.com"),
            new(2, "Marek Novak", "marek.novak@example.com"),
            new(3, "Sofia Turner", "sofia.turner@example.com")
        };

        _addCommand = new RelayCommand(Add, CanAdd);
        _updateCommand = new RelayCommand(Update, CanUpdateOrDelete);
        _deleteCommand = new RelayCommand(Delete, CanUpdateOrDelete);
        _clearCommand = new RelayCommand(Clear);
    }

    public ObservableCollection<PersonRecord> People { get; }

    public RelayCommand AddCommand => _addCommand;

    public RelayCommand UpdateCommand => _updateCommand;

    public RelayCommand DeleteCommand => _deleteCommand;

    public RelayCommand ClearCommand => _clearCommand;

    public PersonRecord? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (!SetProperty(ref _selectedPerson, value))
            {
                return;
            }

            if (value is not null)
            {
                DraftName = value.Name;
                DraftEmail = value.Email;
                StatusMessage = $"Selected person #{value.Id}.";
            }

            RefreshCommandStates();
        }
    }

    public string DraftName
    {
        get => _draftName;
        set
        {
            if (!SetProperty(ref _draftName, value))
            {
                return;
            }

            RefreshCommandStates();
        }
    }

    public string DraftEmail
    {
        get => _draftEmail;
        set
        {
            if (!SetProperty(ref _draftEmail, value))
            {
                return;
            }

            RefreshCommandStates();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool CanAdd()
    {
        return IsDraftValid();
    }

    private bool CanUpdateOrDelete()
    {
        return SelectedPerson is not null;
    }

    private void Add()
    {
        var person = new PersonRecord(_nextId++, DraftName.Trim(), DraftEmail.Trim());
        People.Add(person);
        SelectedPerson = person;
        StatusMessage = $"Added '{person.Name}'.";
    }

    private void Update()
    {
        if (SelectedPerson is null)
        {
            return;
        }

        if (!IsDraftValid())
        {
            StatusMessage = "Name and email are required for update.";
            return;
        }

        SelectedPerson.Name = DraftName.Trim();
        SelectedPerson.Email = DraftEmail.Trim();
        StatusMessage = $"Updated person #{SelectedPerson.Id}.";
    }

    private void Delete()
    {
        if (SelectedPerson is null)
        {
            return;
        }

        var removedName = SelectedPerson.Name;
        People.Remove(SelectedPerson);
        SelectedPerson = null;
        DraftName = string.Empty;
        DraftEmail = string.Empty;
        StatusMessage = $"Deleted '{removedName}'.";
        RefreshCommandStates();
    }

    private void Clear()
    {
        SelectedPerson = null;
        DraftName = string.Empty;
        DraftEmail = string.Empty;
        StatusMessage = "Editor cleared.";
        RefreshCommandStates();
    }

    private bool IsDraftValid()
    {
        return !string.IsNullOrWhiteSpace(DraftName)
            && !string.IsNullOrWhiteSpace(DraftEmail);
    }

    private void RefreshCommandStates()
    {
        _addCommand.NotifyCanExecuteChanged();
        _updateCommand.NotifyCanExecuteChanged();
        _deleteCommand.NotifyCanExecuteChanged();
    }
}
