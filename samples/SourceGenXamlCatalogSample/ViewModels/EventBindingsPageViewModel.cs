using SourceGenXamlCatalogSample.Infrastructure;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class EventBindingsPageViewModel : ViewModelBase
{
    private int _commandCount;
    private int _methodCount;
    private int _rootCount;
    private string _commandStatus = "No command invocation yet.";
    private string _methodStatus = "No method invocation yet.";
    private string _rootStatus = "No root invocation yet.";

    public EventBindingsPageViewModel()
    {
        IncrementCommand = new RelayCommand(_ =>
        {
            _commandCount++;
            CommandStatus = $"IncrementCommand invoked {_commandCount} time(s).";
        });

        SelectItemCommand = new RelayCommand(parameter =>
        {
            _commandCount++;
            CommandStatus = $"SelectItemCommand invoked {_commandCount} time(s). Parameter = {parameter ?? "<null>"}";
        });
    }

    public RelayCommand IncrementCommand { get; }

    public RelayCommand SelectItemCommand { get; }

    public string NextItemLabel { get; set; } = "Catalog item #42";

    public string CommandStatus
    {
        get => _commandStatus;
        private set => SetProperty(ref _commandStatus, value);
    }

    public string MethodStatus
    {
        get => _methodStatus;
        private set => SetProperty(ref _methodStatus, value);
    }

    public string RootStatus
    {
        get => _rootStatus;
        private set => SetProperty(ref _rootStatus, value);
    }

    public void Save()
    {
        _methodCount++;
        MethodStatus = $"Save() invoked {_methodCount} time(s).";
    }

    public void SaveWithArgs(object? sender, object? args)
    {
        _methodCount++;
        var senderName = sender?.GetType().Name ?? "<null>";
        var argsName = args?.GetType().Name ?? "<null>";
        MethodStatus = $"SaveWithArgs invoked {_methodCount} time(s). sender={senderName}, args={argsName}.";
    }

    public void RecordRootAction()
    {
        _rootCount++;
        RootStatus = $"Root action invoked {_rootCount} time(s).";
    }
}
