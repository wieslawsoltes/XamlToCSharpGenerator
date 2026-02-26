using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public partial class TreeViewPage : UserControl
    {
        public TreeViewPage()
        {
            InitializeComponent();
            DataContext = new TreeViewPageViewModel();
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }
    }
}
