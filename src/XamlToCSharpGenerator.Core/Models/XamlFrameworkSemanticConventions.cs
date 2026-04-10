using System.Collections.Immutable;
using System.Linq;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Core.Models;

public sealed class XamlFrameworkSemanticConventions
{
    private readonly ImmutableHashSet<string> _styleDefinitionRootTypeNames;
    private readonly ImmutableHashSet<string> _controlThemeDefinitionRootTypeNames;
    private readonly ImmutableHashSet<string> _includeRootTypeNames;
    private readonly ImmutableHashSet<string> _knownTemplateKinds;
    private readonly ImmutableHashSet<string> _usableDuringInitializationAttributeMetadataNames;

    public static XamlFrameworkSemanticConventions Empty { get; } = new(
        styleTypeContractIds: ImmutableArray<TypeContractId>.Empty,
        controlThemeTypeContractIds: ImmutableArray<TypeContractId>.Empty,
        controlTemplateTypeContractIds: ImmutableArray<TypeContractId>.Empty,
        templateScopeTypeContractIds: ImmutableArray<TypeContractId>.Empty,
        styleDefinitionRootTypeNames: ImmutableArray<string>.Empty,
        controlThemeDefinitionRootTypeNames: ImmutableArray<string>.Empty,
        includeRootTypeNames: ImmutableArray<string>.Empty,
        knownTemplateKinds: ImmutableArray<string>.Empty,
        inheritDataTypeFromItemsAttributeMetadataNames: ImmutableArray<string>.Empty,
        itemContainerTypeMappings: ImmutableDictionary<string, string>.Empty,
        usableDuringInitializationAttributeMetadataNames: ImmutableArray<string>.Empty);

    public XamlFrameworkSemanticConventions(
        ImmutableArray<TypeContractId> styleTypeContractIds,
        ImmutableArray<TypeContractId> controlThemeTypeContractIds,
        ImmutableArray<TypeContractId> controlTemplateTypeContractIds,
        ImmutableArray<TypeContractId> templateScopeTypeContractIds,
        ImmutableArray<string> styleDefinitionRootTypeNames,
        ImmutableArray<string> controlThemeDefinitionRootTypeNames,
        ImmutableArray<string> includeRootTypeNames,
        ImmutableArray<string> knownTemplateKinds,
        ImmutableArray<string> inheritDataTypeFromItemsAttributeMetadataNames,
        ImmutableDictionary<string, string> itemContainerTypeMappings,
        ImmutableArray<string> usableDuringInitializationAttributeMetadataNames)
    {
        StyleTypeContractIds = styleTypeContractIds;
        ControlThemeTypeContractIds = controlThemeTypeContractIds;
        ControlTemplateTypeContractIds = controlTemplateTypeContractIds;
        TemplateScopeTypeContractIds = templateScopeTypeContractIds;
        StyleDefinitionRootTypeNames = styleDefinitionRootTypeNames;
        ControlThemeDefinitionRootTypeNames = controlThemeDefinitionRootTypeNames;
        IncludeRootTypeNames = includeRootTypeNames;
        KnownTemplateKinds = knownTemplateKinds;
        InheritDataTypeFromItemsAttributeMetadataNames = inheritDataTypeFromItemsAttributeMetadataNames;
        ItemContainerTypeMappings = itemContainerTypeMappings;

        _styleDefinitionRootTypeNames = styleDefinitionRootTypeNames.ToImmutableHashSet(System.StringComparer.Ordinal);
        _controlThemeDefinitionRootTypeNames = controlThemeDefinitionRootTypeNames.ToImmutableHashSet(System.StringComparer.Ordinal);
        _includeRootTypeNames = includeRootTypeNames.ToImmutableHashSet(System.StringComparer.Ordinal);
        _knownTemplateKinds = knownTemplateKinds.ToImmutableHashSet(System.StringComparer.Ordinal);
        _usableDuringInitializationAttributeMetadataNames =
            usableDuringInitializationAttributeMetadataNames.ToImmutableHashSet(System.StringComparer.Ordinal);
    }

    public ImmutableArray<TypeContractId> StyleTypeContractIds { get; }

    public ImmutableArray<TypeContractId> ControlThemeTypeContractIds { get; }

    public ImmutableArray<TypeContractId> ControlTemplateTypeContractIds { get; }

    public ImmutableArray<TypeContractId> TemplateScopeTypeContractIds { get; }

    public ImmutableArray<string> StyleDefinitionRootTypeNames { get; }

    public ImmutableArray<string> ControlThemeDefinitionRootTypeNames { get; }

    public ImmutableArray<string> IncludeRootTypeNames { get; }

    public ImmutableArray<string> KnownTemplateKinds { get; }

    public ImmutableArray<string> InheritDataTypeFromItemsAttributeMetadataNames { get; }

    public ImmutableDictionary<string, string> ItemContainerTypeMappings { get; }

    public bool IsStyleDefinitionRootTypeName(string localName)
    {
        return _styleDefinitionRootTypeNames.Contains(localName);
    }

    public bool IsControlThemeDefinitionRootTypeName(string localName)
    {
        return _controlThemeDefinitionRootTypeNames.Contains(localName);
    }

    public bool IsIncludeRootTypeName(string localName)
    {
        return _includeRootTypeNames.Contains(localName);
    }

    public bool IsKnownTemplateKind(string localName)
    {
        return _knownTemplateKinds.Contains(localName);
    }

    public bool IsUsableDuringInitializationAttribute(string metadataName)
    {
        return _usableDuringInitializationAttributeMetadataNames.Contains(metadataName);
    }
}
