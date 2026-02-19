using System.Collections.ObjectModel;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class BasicsPageViewModel : ViewModelBase
{
    public string Heading { get; } = "Object Graph + Property Assignment";

    public string Description { get; } = "Shows nested controls, attached properties, content properties, and collection templates generated to C#.";

    public ObservableCollection<string> Tags { get; } =
    [
        "x:Class",
        "x:Name",
        "x:DataType",
        "x:CompileBindings",
        "ContentProperty",
        "ChildrenCollection",
        "DataTemplate",
        "ItemsPanelTemplate"
    ];
}
