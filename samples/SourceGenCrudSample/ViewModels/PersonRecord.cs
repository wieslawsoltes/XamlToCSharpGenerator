namespace SourceGenCrudSample.ViewModels;

public sealed class PersonRecord : ViewModelBase
{
    private string _name;
    private string _email;

    public PersonRecord(int id, string name, string email)
    {
        Id = id;
        _name = name;
        _email = email;
    }

    public int Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }
}
