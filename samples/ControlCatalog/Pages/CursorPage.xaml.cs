using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public partial class CursorPage : UserControl
    {
        public CursorPage()
        {
            this.InitializeComponent();
            DataContext = new CursorPageViewModel();
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }
    }
}
