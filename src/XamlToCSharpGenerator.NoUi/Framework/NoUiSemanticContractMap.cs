using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.NoUi.Framework;

public static class NoUiSemanticContractMap
{
    public static SemanticContractMap Instance { get; } = SemanticContractMaps.NoUiDefault;
}
