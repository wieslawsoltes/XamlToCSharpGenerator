namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class GlobalXmlnsPageViewModel : ViewModelBase
{
    public string Title { get; } = "Global XMLNS Imports";

    public string Description { get; } = "This page intentionally omits local vm/catalog xmlns declarations. Those prefixes come from project-level SourceGen global mappings.";

    public string DemoLabel { get; } = "Control created without local xmlns declarations.";

    public string Summary { get; } = "Configured through AvaloniaSourceGenGlobalXmlnsPrefixes with optional implicit default namespace support.";
}
