using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Configuration;

public sealed record SemanticTypeContract(
    TypeContractId Id,
    ImmutableArray<string> MetadataNames,
    bool IsRequired,
    string FeatureTag);

public sealed class SemanticContractMap
{
    private readonly ImmutableDictionary<TypeContractId, SemanticTypeContract> _typeContracts;
    private readonly ImmutableArray<SemanticTypeContract> _orderedTypeContracts;

    public SemanticContractMap(
        string mapId,
        string frameworkId,
        IEnumerable<SemanticTypeContract> typeContracts)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(mapId));
        }

        if (string.IsNullOrWhiteSpace(frameworkId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(frameworkId));
        }

        if (typeContracts is null)
        {
            throw new ArgumentNullException(nameof(typeContracts));
        }

        MapId = mapId;
        FrameworkId = frameworkId;

        var builder = ImmutableDictionary.CreateBuilder<TypeContractId, SemanticTypeContract>();
        foreach (var contract in typeContracts)
        {
            builder[contract.Id] = NormalizeContract(contract);
        }

        _typeContracts = builder.ToImmutable();
        _orderedTypeContracts = _typeContracts.Values
            .OrderBy(static contract => contract.Id)
            .ToImmutableArray();
        CatalogCacheKey = BuildCatalogCacheKey(mapId, frameworkId, _orderedTypeContracts);
    }

    public string MapId { get; }

    public string FrameworkId { get; }

    public string CatalogCacheKey { get; }

    public ImmutableArray<SemanticTypeContract> TypeContracts => _orderedTypeContracts;

    public bool TryGetTypeContract(TypeContractId id, out SemanticTypeContract contract)
    {
        return _typeContracts.TryGetValue(id, out contract!);
    }

    public SemanticTypeContract GetTypeContract(TypeContractId id)
    {
        if (!_typeContracts.TryGetValue(id, out var contract))
        {
            throw new KeyNotFoundException("Type contract '" + id + "' is not defined in '" + MapId + "'.");
        }

        return contract;
    }

    private static SemanticTypeContract NormalizeContract(SemanticTypeContract contract)
    {
        if (contract.MetadataNames.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Type contract '" + contract.Id + "' must contain at least one metadata name.",
                nameof(contract));
        }

        if (string.IsNullOrWhiteSpace(contract.FeatureTag))
        {
            throw new ArgumentException(
                "Type contract '" + contract.Id + "' must provide a feature tag.",
                nameof(contract));
        }

        var normalizedNames = ImmutableArray.CreateBuilder<string>(contract.MetadataNames.Length);
        foreach (var metadataName in contract.MetadataNames)
        {
            if (string.IsNullOrWhiteSpace(metadataName))
            {
                throw new ArgumentException(
                    "Type contract '" + contract.Id + "' contains an empty metadata name.",
                    nameof(contract));
            }

            normalizedNames.Add(metadataName.Trim());
        }

        return contract with
        {
            MetadataNames = normalizedNames.ToImmutable(),
            FeatureTag = contract.FeatureTag.Trim()
        };
    }

    private static string BuildCatalogCacheKey(
        string mapId,
        string frameworkId,
        ImmutableArray<SemanticTypeContract> contracts)
    {
        var builder = new StringBuilder();
        builder.Append(mapId);
        builder.Append('|');
        builder.Append(frameworkId);

        for (var contractIndex = 0; contractIndex < contracts.Length; contractIndex++)
        {
            var contract = contracts[contractIndex];
            builder.Append('|');
            builder.Append((int)contract.Id);
            builder.Append(':');
            builder.Append(contract.IsRequired ? '1' : '0');
            builder.Append(':');
            builder.Append(contract.FeatureTag);
            builder.Append(':');

            for (var metadataIndex = 0; metadataIndex < contract.MetadataNames.Length; metadataIndex++)
            {
                builder.Append(contract.MetadataNames[metadataIndex]);
                builder.Append(';');
            }
        }

        return builder.ToString();
    }
}

public static class SemanticContractMaps
{
    public static SemanticContractMap AvaloniaDefault { get; } = CreateAvaloniaDefault();

    public static SemanticContractMap NoUiDefault { get; } = CreateNoUiDefault();

    private static SemanticContractMap CreateAvaloniaDefault()
    {
        return new SemanticContractMap(
            mapId: "Avalonia.Default",
            frameworkId: FrameworkProfileIds.Avalonia,
            typeContracts:
            [
                Create(TypeContractId.SystemObject, false, "bcl", "System.Object"),
                Create(TypeContractId.SystemActionOfT1T2, false, "bcl", "System.Action`2"),
                Create(TypeContractId.SystemTaskOfT, false, "bcl", "System.Threading.Tasks.Task`1"),
                Create(TypeContractId.SystemTask, false, "bcl", "System.Threading.Tasks.Task"),
                Create(TypeContractId.SystemObservableOfT, false, "bcl", "System.IObservable`1"),
                Create(TypeContractId.SystemICommand, false, "bcl", "System.Windows.Input.ICommand"),
                Create(TypeContractId.SystemDelegate, false, "bcl", "System.Delegate"),
                Create(TypeContractId.SystemEventHandlerOfT, false, "bcl", "System.EventHandler`1"),
                Create(TypeContractId.SystemEventArgs, false, "bcl", "System.EventArgs"),
                Create(TypeContractId.SystemListOfT, false, "bcl", "System.Collections.Generic.List`1"),
                Create(TypeContractId.StyledElement, false, "styling", "Avalonia.StyledElement"),
                Create(TypeContractId.NameScope, false, "namescope", "Avalonia.Controls.NameScope"),
                Create(TypeContractId.AvaloniaInamed, false, "namescope", "Avalonia.INamed"),
                Create(TypeContractId.AvaloniaMarkupExtensionBase, false, "markup", "Avalonia.Markup.Xaml.MarkupExtension"),
                Create(TypeContractId.AvaloniaBindingBase, false, "binding", "Avalonia.Data.BindingBase"),
                Create(TypeContractId.AvaloniaBindingInterface, false, "binding", "Avalonia.Data.IBinding"),
                Create(TypeContractId.AvaloniaBindingInterface2, false, "binding", "Avalonia.Data.Core.IBinding2"),
                Create(TypeContractId.AvaloniaBinding, false, "binding", "Avalonia.Data.Binding"),
                Create(TypeContractId.AvaloniaReflectionBindingExtension, false, "binding", "Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension"),
                Create(TypeContractId.AvaloniaRelativeSource, false, "binding", "Avalonia.Data.RelativeSource"),
                Create(TypeContractId.AvaloniaBindingPriority, false, "binding", "Avalonia.Data.BindingPriority"),
                Create(TypeContractId.AvaloniaProperty, false, "binding", "Avalonia.AvaloniaProperty"),
                Create(TypeContractId.AvaloniaRoutedEventHandler, false, "events", "Avalonia.Interactivity.RoutedEventHandler"),
                Create(TypeContractId.AvaloniaRoutedEventArgs, false, "events", "Avalonia.Interactivity.RoutedEventArgs"),
                Create(TypeContractId.AvaloniaRoutedEvent, false, "events", "Avalonia.Interactivity.RoutedEvent"),
                Create(TypeContractId.AvaloniaGenericRoutedEvent, false, "events", "Avalonia.Interactivity.RoutedEvent`1"),
                Create(TypeContractId.Application, false, "hotdesign", "Avalonia.Application"),
                Create(TypeContractId.Styles, false, "styling", "Avalonia.Styling.Styles"),
                Create(TypeContractId.Style, false, "styling", "Avalonia.Styling.Style"),
                Create(TypeContractId.ControlTheme, false, "styling", "Avalonia.Styling.ControlTheme"),
                Create(TypeContractId.AvaloniaControl, false, "controls", "Avalonia.Controls.Control"),
                Create(TypeContractId.AvaloniaPanel, false, "controls", "Avalonia.Controls.Panel"),
                Create(TypeContractId.ItemsControl, false, "controls", "Avalonia.Controls.ItemsControl"),
                Create(TypeContractId.ContentControl, false, "controls", "Avalonia.Controls.ContentControl"),
                Create(TypeContractId.AvaloniaColor, false, "media", "Avalonia.Media.Color"),
                Create(TypeContractId.AvaloniaIBrush, false, "media", "Avalonia.Media.IBrush"),
                Create(TypeContractId.AvaloniaBrush, false, "media", "Avalonia.Media.Brush"),
                Create(TypeContractId.AvaloniaSolidColorBrush, false, "media", "Avalonia.Media.SolidColorBrush"),
                Create(TypeContractId.AvaloniaCursor, false, "input", "Avalonia.Input.Cursor"),
                Create(TypeContractId.AvaloniaStandardCursorType, false, "input", "Avalonia.Input.StandardCursorType"),
                Create(TypeContractId.AvaloniaKeyGesture, false, "input", "Avalonia.Input.KeyGesture"),
                Create(TypeContractId.AvaloniaKey, false, "input", "Avalonia.Input.Key"),
                Create(TypeContractId.AvaloniaKeyModifiers, false, "input", "Avalonia.Input.KeyModifiers"),
                Create(TypeContractId.AvaloniaMatrix, false, "media", "Avalonia.Matrix"),
                Create(TypeContractId.AvaloniaTransformOperations, false, "media", "Avalonia.Media.Transformation.TransformOperations"),
                Create(TypeContractId.ResourceDictionary, false, "resources", "Avalonia.Controls.ResourceDictionary"),
                Create(TypeContractId.ResourceInclude, false, "includes", "Avalonia.Markup.Xaml.Styling.ResourceInclude"),
                Create(TypeContractId.MergeResourceInclude, false, "includes", "Avalonia.Markup.Xaml.Styling.MergeResourceInclude"),
                Create(TypeContractId.StyleInclude, false, "includes", "Avalonia.Markup.Xaml.Styling.StyleInclude"),
                Create(TypeContractId.MarkupControlTemplate, false, "templates", "Avalonia.Markup.Xaml.Templates.ControlTemplate"),
                Create(TypeContractId.ControlsControlTemplate, false, "templates", "Avalonia.Controls.Templates.ControlTemplate"),
                Create(TypeContractId.ControlTemplateInterface, false, "templates", "Avalonia.Controls.Templates.IControlTemplate"),
                Create(TypeContractId.ItemsPanelTemplate, false, "templates", "Avalonia.Markup.Xaml.Templates.ItemsPanelTemplate"),
                Create(TypeContractId.MarkupTemplate, false, "templates", "Avalonia.Markup.Xaml.Templates.Template"),
                Create(TypeContractId.AvaloniaTemplateBinding, false, "binding", "Avalonia.Data.TemplateBinding"),
                Create(TypeContractId.StaticResourceExtension, false, "markup", "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension"),
                Create(TypeContractId.DynamicResourceExtension, false, "markup", "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension"),
                Create(TypeContractId.OnPlatformExtension, false, "markup", "Avalonia.Markup.Xaml.MarkupExtensions.OnPlatformExtension"),
                Create(TypeContractId.OnFormFactorExtension, false, "markup", "Avalonia.Markup.Xaml.MarkupExtensions.OnFormFactorExtension"),
                Create(TypeContractId.OnMarkupExtension, false, "markup", "Avalonia.Markup.Xaml.MarkupExtensions.On")
            ]);
    }

    private static SemanticContractMap CreateNoUiDefault()
    {
        return new SemanticContractMap(
            mapId: "NoUi.Default",
            frameworkId: FrameworkProfileIds.NoUi,
            typeContracts:
            [
                Create(TypeContractId.SystemObject, false, "bcl", "System.Object"),
                Create(TypeContractId.SystemActionOfT1T2, false, "bcl", "System.Action`2"),
                Create(TypeContractId.SystemTaskOfT, false, "bcl", "System.Threading.Tasks.Task`1"),
                Create(TypeContractId.SystemTask, false, "bcl", "System.Threading.Tasks.Task"),
                Create(TypeContractId.SystemObservableOfT, false, "bcl", "System.IObservable`1"),
                Create(TypeContractId.SystemICommand, false, "bcl", "System.Windows.Input.ICommand"),
                Create(TypeContractId.SystemDelegate, false, "bcl", "System.Delegate"),
                Create(TypeContractId.SystemEventHandlerOfT, false, "bcl", "System.EventHandler`1"),
                Create(TypeContractId.SystemEventArgs, false, "bcl", "System.EventArgs"),
                Create(TypeContractId.SystemListOfT, false, "bcl", "System.Collections.Generic.List`1")
            ]);
    }

    private static SemanticTypeContract Create(
        TypeContractId id,
        bool isRequired,
        string featureTag,
        params string[] metadataNames)
    {
        return new SemanticTypeContract(
            id,
            metadataNames.ToImmutableArray(),
            isRequired,
            featureTag);
    }
}
