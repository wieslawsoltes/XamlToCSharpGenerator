using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SourceGenCrudSample.ViewModels;

namespace SourceGenCrudSample;

public partial class App : Application
{
    public override void Initialize()
    {
        InitializeComponent();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
