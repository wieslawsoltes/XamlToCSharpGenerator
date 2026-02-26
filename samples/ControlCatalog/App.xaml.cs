using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ControlCatalog.ViewModels;

namespace ControlCatalog
{
    public partial class App : Application
    {
        private readonly Styles _themeStylesContainer = new();
        private FluentTheme? _fluentTheme;
        private IStyle? _colorPickerFluent;

        public App()
        {
            DataContext = new ApplicationViewModel();
        }

        public override void Initialize()
        {
            Styles.Add(_themeStylesContainer);

            InitializeComponent(true);

            _fluentTheme = (FluentTheme)Resources["FluentTheme"]!;
            _colorPickerFluent = (IStyle)Resources["ColorPickerFluent"]!;

            EnsureFluentCatalogTheme();
        }

        public static void EnsureFluentCatalogTheme()
        {
            var app = (App)Current!;

            if (app._themeStylesContainer.Count == 0)
            {
                app._themeStylesContainer.Add(new Style());
                app._themeStylesContainer.Add(new Style());
            }

            app._themeStylesContainer[0] = app._fluentTheme!;
            app._themeStylesContainer[1] = app._colorPickerFluent!;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
            {
                singleViewLifetime.MainView = new MainView { DataContext = new MainWindowViewModel() };
            }

            if (this.TryGetFeature<IActivatableLifetime>() is { } activatableApplicationLifetime)
            {
                activatableApplicationLifetime.Activated += (sender, args) =>
                    Console.WriteLine($"App activated: {args.Kind}");
                activatableApplicationLifetime.Deactivated += (sender, args) =>
                    Console.WriteLine($"App deactivated: {args.Kind}");
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
