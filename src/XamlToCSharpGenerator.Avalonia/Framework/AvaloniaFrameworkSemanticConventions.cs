using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Framework;

public sealed class AvaloniaFrameworkSemanticConventions
{
    public static XamlFrameworkSemanticConventions Instance { get; } = new(
        styleTypeContractIds: ImmutableArray.Create(TypeContractId.Style, TypeContractId.Styles),
        controlThemeTypeContractIds: ImmutableArray.Create(TypeContractId.ControlTheme),
        controlTemplateTypeContractIds: ImmutableArray.Create(
            TypeContractId.MarkupControlTemplate,
            TypeContractId.ControlsControlTemplate,
            TypeContractId.ControlTemplateInterface),
        templateScopeTypeContractIds: ImmutableArray.Create(
            TypeContractId.MarkupTemplate,
            TypeContractId.MarkupControlTemplate,
            TypeContractId.ControlsControlTemplate,
            TypeContractId.ItemsPanelTemplate),
        styleDefinitionRootTypeNames: ImmutableArray.Create("Style", "Styles"),
        controlThemeDefinitionRootTypeNames: ImmutableArray.Create("ControlTheme"),
        includeRootTypeNames: ImmutableArray.Create("ResourceInclude", "MergeResourceInclude", "StyleInclude"),
        knownTemplateKinds: ImmutableArray.Create("DataTemplate", "TreeDataTemplate", "ControlTemplate", "ItemsPanelTemplate"),
        inheritDataTypeFromItemsAttributeMetadataNames: ImmutableArray.Create(
            "Avalonia.Metadata.InheritDataTypeFromItemsAttribute",
            "global::Avalonia.Metadata.InheritDataTypeFromItemsAttribute"),
        itemContainerTypeMappings: ImmutableDictionary<string, string>.Empty
            .Add("ListBox", "ListBoxItem")
            .Add("ComboBox", "ComboBoxItem")
            .Add("Menu", "MenuItem")
            .Add("MenuItem", "MenuItem")
            .Add("TabStrip", "TabStripItem")
            .Add("TabControl", "TabItem")
            .Add("TreeView", "TreeViewItem"),
        usableDuringInitializationAttributeMetadataNames: ImmutableArray.Create(
            "Avalonia.Metadata.UsableDuringInitializationAttribute",
            "global::Avalonia.Metadata.UsableDuringInitializationAttribute"));

    private AvaloniaFrameworkSemanticConventions()
    {
    }
}
