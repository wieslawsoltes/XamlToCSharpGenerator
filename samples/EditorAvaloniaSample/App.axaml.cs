using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EditorAvaloniaSample.Services;
using EditorAvaloniaSample.ViewModels;

namespace EditorAvaloniaSample;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var workspaceService = new EditorSampleWorkspaceService();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(workspaceService)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
