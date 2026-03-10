using System;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlXmlNamespaceNavigationService
{
    public static bool TryResolvePrefixDefinitionAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlDefinitionLocation definitionLocation)
    {
        definitionLocation = default!;
        if (analysis.XmlDocument?.Root is null)
        {
            return false;
        }

        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        if (!TryResolvePrefixAtPosition(analysis, position, context, out var prefix, out var scopeElement))
        {
            return false;
        }

        foreach (var current in scopeElement.AncestorsAndSelf())
        {
            foreach (var attribute in current.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                var declaredPrefix = attribute.Name.LocalName == "xmlns"
                    ? string.Empty
                    : attribute.Name.LocalName;
                if (!string.Equals(declaredPrefix, prefix, StringComparison.Ordinal) ||
                    !XamlXmlSourceRangeService.TryCreateNamespaceDeclarationPrefixRange(
                        analysis.Document.Text,
                        attribute,
                        out var prefixRange))
                {
                    continue;
                }

                definitionLocation = new XamlDefinitionLocation(
                    UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                    prefixRange);
                return true;
            }
        }

        return false;
    }

    private static bool TryResolvePrefixAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        XamlCompletionContext context,
        out string prefix,
        out XElement scopeElement)
    {
        prefix = string.Empty;
        scopeElement = null!;

        switch (context.Kind)
        {
            case XamlCompletionContextKind.ElementName:
            case XamlCompletionContextKind.QualifiedPropertyElement:
                if (!TryResolvePrefixFromToken(analysis.Document.Text, position, context, out prefix) ||
                    !XamlXmlSourceRangeService.TryFindElementNameAtPosition(
                        analysis.Document.Text,
                        analysis.XmlDocument,
                        position,
                        out scopeElement,
                        out _))
                {
                    return false;
                }

                return true;
            case XamlCompletionContextKind.AttributeName:
                if (!TryResolvePrefixFromToken(analysis.Document.Text, position, context, out prefix) ||
                    !XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                        analysis.Document.Text,
                        analysis.XmlDocument,
                        position,
                        out scopeElement,
                        out _,
                        out _,
                        out _))
                {
                    return false;
                }

                return true;
            default:
                return false;
        }
    }

    private static bool TryResolvePrefixFromToken(
        string text,
        SourcePosition position,
        XamlCompletionContext context,
        out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(context.Token))
        {
            return false;
        }

        var separatorIndex = context.Token.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var offset = TextCoordinateHelper.GetOffset(text, position);
        var relativeOffset = offset - context.TokenStartOffset;
        if (relativeOffset < 0 || relativeOffset > separatorIndex)
        {
            return false;
        }

        prefix = context.Token.Substring(0, separatorIndex);
        return !string.IsNullOrWhiteSpace(prefix);
    }
}
