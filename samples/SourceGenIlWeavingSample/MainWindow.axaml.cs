using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SourceGenIlWeavingSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
