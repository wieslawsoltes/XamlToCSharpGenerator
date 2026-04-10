using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Avalonia.Framework;

public static class AvaloniaSemanticContractMap
{
    public static SemanticContractMap Instance { get; } = SemanticContractMaps.AvaloniaDefault;
}
