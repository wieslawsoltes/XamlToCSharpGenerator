namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private int _selectedTabIndex;

    public string Title { get; } = "SourceGen XAML Feature Catalog";

    public string Summary { get; } = "Tabbed catalog of supported SourceGen features for Avalonia XAML, with live examples per feature group.";

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public BasicsPageViewModel Basics { get; } = new();

    public BindingsPageViewModel Bindings { get; } = new();

    public RelativeSourcePageViewModel RelativeSource { get; } = new();

    public MarkupExtensionsPageViewModel MarkupExtensions { get; } = new();

    public ExpressionBindingsPageViewModel Expressions { get; } = new();

    public EventBindingsPageViewModel EventBindings { get; } = new();

    public GlobalXmlnsPageViewModel GlobalXmlns { get; } = new();

    public ConditionalXamlPageViewModel ConditionalXaml { get; } = new();

    public TemplatesPageViewModel Templates { get; } = new();

    public ResourcesIncludesPageViewModel Resources { get; } = new();

    public HotDesignStudioViewModel HotDesign { get; } = new();
}
