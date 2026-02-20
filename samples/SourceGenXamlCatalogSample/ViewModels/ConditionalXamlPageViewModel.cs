namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class ConditionalXamlPageViewModel : ViewModelBase
{
    public string Title { get; } = "Conditional XAML";

    public string Description { get; } =
        "Conditional namespace prefixes gate elements, attributes, styles, and resource registrations at compile-time.";

    public string Summary { get; } =
        "False branches are pruned before semantic/type binding, so unreachable markup does not emit AXSG0100 unknown-type noise.";

    public string TypePresentMessage { get; } =
        "This line is emitted because ApiInformation.IsTypePresent('Avalonia.Controls.TextBlock') evaluates true.";

    public string TypeMissingMessage { get; } =
        "You should never see this line because its namespace condition evaluates false.";

    public string StyleConditionMessage { get; } =
        "This style comes from a condition-true prefix; the condition-false style is not emitted.";

    public string PropertyConditionMessage { get; } =
        "Foreground is conditionally overridden only when the target property exists.";

    public string ContractPresentMessage { get; } =
        "Contract-present branch is active.";

    public string ContractMissingMessage { get; } =
        "Contract-missing branch is skipped.";
}
