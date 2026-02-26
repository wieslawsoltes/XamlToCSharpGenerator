using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public partial class ListBoxPage : UserControl
    {
        public ListBoxPage()
        {
            InitializeComponent();
            DataContext = new ListBoxPageViewModel();
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }
    }
}
