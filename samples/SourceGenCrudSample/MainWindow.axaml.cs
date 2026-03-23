using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SourceGenCrudSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
