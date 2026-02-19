using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System.Linq;
using SourceGenCrudSample.ViewModels;

namespace SourceGenCrudSample;

public partial class App : Application
{
    public override void Initialize()
    {
        InitializeComponent();

        // SourceGen v1 currently doesn't project Application.Styles property-elements from App.axaml.
        // Ensure controls are themed so the sample UI is visible.
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
