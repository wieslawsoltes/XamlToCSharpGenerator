using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using SourceGenXamlCatalogSample.ViewModels;
using System.Linq;

namespace SourceGenXamlCatalogSample;

public partial class App : Application
{
    public override void Initialize()
    {
        InitializeComponent();

        // SourceGen currently doesn't fully project Application.Styles from App.axaml.
        if (!Styles.OfType<FluentTheme>().Any())
        {
            Styles.Insert(0, new FluentTheme());
        }
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
