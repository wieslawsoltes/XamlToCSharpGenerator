using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public partial class ComboBoxPage : UserControl
    {
        public ComboBoxPage()
        {
            this.InitializeComponent();
            DataContext = new ComboBoxPageViewModel();
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
            var fontComboBox = this.Get<ComboBox>("fontComboBox");
            fontComboBox.ItemsSource = FontManager.Current.SystemFonts;
            fontComboBox.SelectedIndex = 0;
        }
    }
}
