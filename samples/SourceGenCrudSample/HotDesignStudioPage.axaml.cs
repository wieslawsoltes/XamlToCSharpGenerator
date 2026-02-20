using Avalonia.Controls;
using SourceGenCrudSample.ViewModels;
using XamlToCSharpGenerator.Runtime;

namespace SourceGenCrudSample;

public partial class HotDesignStudioPage : UserControl
{
    public HotDesignStudioPage()
    {
        InitializeComponent();
        DataContext ??= new HotDesignStudioViewModel();
    }

    private HotDesignStudioViewModel? ViewModel => DataContext as HotDesignStudioViewModel;

    private void OnElementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is SourceGenHotDesignElementNode node)
        {
            ViewModel.SelectedElement = node;
        }
    }

    private void OnPropertySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is SourceGenHotDesignPropertyEntry entry)
        {
            ViewModel.SelectedProperty = entry;
        }
    }

    private void OnToolboxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is SourceGenHotDesignToolboxItem item)
        {
            ViewModel.SelectedToolboxItem = item;
        }
    }
}
