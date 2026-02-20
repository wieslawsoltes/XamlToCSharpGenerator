using Avalonia.Controls;
using SourceGenXamlCatalogSample.ViewModels;

namespace SourceGenXamlCatalogSample.Pages;

public partial class EventBindingsPage : UserControl
{
    public EventBindingsPage()
    {
        InitializeComponent();
    }

    private void HandleRootAction()
    {
        if (DataContext is EventBindingsPageViewModel viewModel)
        {
            viewModel.RecordRootAction();
        }
    }
}
