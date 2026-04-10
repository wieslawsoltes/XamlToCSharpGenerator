using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkDeferredDictionaryEmitterAdapter : IXamlFrameworkDeferredDictionaryEmitterAdapter
{
    public static AvaloniaFrameworkDeferredDictionaryEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkDeferredDictionaryEmitterAdapter()
    {
    }

    public string NormalizeDictionaryKeyExpression(string propertyName, string keyExpression)
    {
        if (!string.Equals(propertyName, "ThemeDictionaries", System.StringComparison.Ordinal))
        {
            return keyExpression;
        }

        return keyExpression switch
        {
            "\"Default\"" => "global::Avalonia.Styling.ThemeVariant.Default",
            "\"Dark\"" => "global::Avalonia.Styling.ThemeVariant.Dark",
            "\"Light\"" => "global::Avalonia.Styling.ThemeVariant.Light",
            _ => keyExpression
        };
    }

    public bool ShouldApplyMergedResourceInclude(ResolvedObjectNode node)
    {
        return node.HasSemantic(ResolvedObjectNodeSemanticFlags.IsResourceInclude);
    }

    public bool ShouldApplyStyleInclude(ResolvedObjectNode node)
    {
        return node.HasSemantic(ResolvedObjectNodeSemanticFlags.IsStyleInclude);
    }

    public string BuildCreateDeferredServiceProviderExpression(
        string serviceProviderReference,
        string rootReference,
        string targetObjectReference,
        string targetPropertyExpression,
        string baseUriExpression,
        string parentStackExpression)
    {
        return "__AXSGObjectGraph.CreateDeferredServiceProvider(" +
               serviceProviderReference + ", " +
               rootReference + ", " +
               targetObjectReference + ", " +
               targetPropertyExpression + ", " +
               baseUriExpression + ", " +
               parentStackExpression + ")";
    }

    public string BuildDictionaryAddStatement(
        string dictionaryReference,
        string keyExpression,
        string valueExpression,
        string documentUriExpression,
        bool isShared = true)
    {
        return "__AXSGObjectGraph.TryAddToDictionary(" +
               dictionaryReference + ", " +
               keyExpression + ", " +
               valueExpression + ", " +
               documentUriExpression +
               (isShared ? string.Empty : ", false") +
               ");";
    }
}
