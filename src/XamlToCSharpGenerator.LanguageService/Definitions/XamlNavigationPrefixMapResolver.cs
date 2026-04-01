using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlNavigationPrefixMapResolver
{
    public static ImmutableDictionary<string, string> Resolve(
        XamlAnalysisResult analysis,
        SourcePosition position)
    {
        if (TryResolveFromAttribute(analysis, position, out var attributePrefixMap))
        {
            return attributePrefixMap;
        }

        if (TryResolveFromElement(analysis, position, out var elementPrefixMap))
        {
            return elementPrefixMap;
        }

        return analysis.PrefixMap;
    }

    private static bool TryResolveFromAttribute(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out ImmutableDictionary<string, string> prefixMap)
    {
        prefixMap = analysis.PrefixMap;
        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out var element,
                out _,
                out _,
                out _))
        {
            return false;
        }

        prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        return true;
    }

    private static bool TryResolveFromElement(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out ImmutableDictionary<string, string> prefixMap)
    {
        prefixMap = analysis.PrefixMap;
        if (!XamlXmlSourceRangeService.TryFindElementNameAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out var element,
                out _))
        {
            return false;
        }

        prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        return true;
    }
}
