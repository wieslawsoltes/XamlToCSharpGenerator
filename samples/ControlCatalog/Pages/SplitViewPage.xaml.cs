using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public partial class SplitViewPage : UserControl
    {
        public SplitViewPage()
        {
            this.InitializeComponent();
            DataContext = new SplitViewPageViewModel();
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }
    }
}
