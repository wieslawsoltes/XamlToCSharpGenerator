using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ControlCatalog.Pages
{
    public partial class ThemePage : UserControl
    {
        public static ThemeVariant Pink { get; } = new("Pink", ThemeVariant.Light);
        
        public ThemePage()
        {
            InitializeComponent();

            var selector = this.Get<ComboBox>("Selector");
            var themeVariantScope = this.Get<ThemeVariantScope>("ThemeVariantScope");

            selector.ItemsSource = new[]
            {
                ThemeVariant.Default,
                ThemeVariant.Dark,
                ThemeVariant.Light,
                Pink
            };
            selector.SelectedIndex = 0;

            selector.SelectionChanged += (_, _) =>
            {
                if (selector.SelectedItem is ThemeVariant theme)
                {
                    themeVariantScope.RequestedThemeVariant = theme;
                }
            };
        }
    }
}
