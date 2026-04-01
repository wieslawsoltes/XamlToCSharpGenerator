using System.Collections.ObjectModel;

namespace EditorAvaloniaSample.ViewModels;

public sealed class EditorPreviewDataViewModel
{
    public EditorPreviewDataViewModel()
    {
        Actions = new ObservableCollection<EditorActionItemViewModel>
        {
            new("Open Workspace", "Resolve the sample project, open a document URI, and keep diagnostics live."),
            new("Inspect Bindings", "Use the typed data model so the editor can resolve members and template shapes."),
            new("Apply Changes", "Edit the AXAML in place and watch the diagnostics pane react immediately.")
        };

        Tools = new ObservableCollection<EditorPaletteToolViewModel>
        {
            new("Border", "Layout", "Wrap cards and editor panes with a stable surface."),
            new("StackPanel", "Layout", "Group smaller regions before promoting them into reusable controls."),
            new("ItemsControl", "Data", "Prototype templated lists against the same viewmodel surface."),
            new("ComboBox", "Input", "Check enum-like suggestions and item templates."),
            new("ToggleSwitch", "Input", "Exercise property lookup and fluent control metadata.")
        };
    }

    public string Title { get; } = "AXAML Editor Workspace";

    public string Subtitle { get; } = "This document is loaded into AxamlTextEditor from a real file path inside the sample project.";

    public string SearchText { get; set; } = "workspace diagnostics";

    public string PrimaryActionLabel { get; } = "Open Sample";

    public string SecondaryActionLabel { get; } = "Publish Layout";

    public ObservableCollection<EditorActionItemViewModel> Actions { get; }

    public ObservableCollection<EditorPaletteToolViewModel> Tools { get; }
}
