using System;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Hover;

public sealed class XamlHoverService
{
    public XamlHoverInfo? GetHover(XamlAnalysisResult analysis, SourcePosition position)
    {
        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        if (context.Kind == XamlCompletionContextKind.Unknown)
        {
            return null;
        }

        var prefixMap = XamlXmlNamespaceResolver.BuildPrefixMap(analysis.ParsedDocument);

        if (context.Kind == XamlCompletionContextKind.ElementName && analysis.TypeIndex is not null)
        {
            if (XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                    prefixMap,
                    context.Token,
                    out var xmlNamespace,
                    out var xmlTypeName) &&
                analysis.TypeIndex.TryGetType(xmlNamespace, xmlTypeName, out var typeInfo) &&
                typeInfo is not null)
            {
                return new XamlHoverInfo(
                    "**Element**\n\n`" + typeInfo.FullTypeName + "`",
                    BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
            }
        }

        if (context.Kind == XamlCompletionContextKind.AttributeName &&
            !string.IsNullOrWhiteSpace(context.CurrentElementName) &&
            analysis.TypeIndex is not null)
        {
            if (XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                    prefixMap,
                    context.CurrentElementName,
                    out var elementXmlNamespace,
                    out var elementTypeName) &&
                analysis.TypeIndex.TryGetType(elementXmlNamespace, elementTypeName, out var typeInfo) &&
                typeInfo is not null)
            {
                foreach (var property in typeInfo.Properties)
                {
                    if (!string.Equals(property.Name, context.Token, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var category = property.IsAttached ? "Attached Property" : "Property";
                    return new XamlHoverInfo(
                        "**" + category + "**\n\n`" + property.Name + " : " + property.TypeName + "`",
                        BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
                }
            }

            if (context.Token.StartsWith("x:", StringComparison.Ordinal))
            {
                return new XamlHoverInfo(
                    "**XAML Directive**\n\n`" + context.Token + "`",
                    BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
            }
        }

        return null;
    }

    private static SourceRange BuildRange(string text, int startOffset, int endOffset)
    {
        var boundedStart = Math.Max(0, Math.Min(startOffset, text.Length));
        var boundedEnd = Math.Max(boundedStart, Math.Min(endOffset, text.Length));

        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, boundedStart),
            TextCoordinateHelper.GetPosition(text, boundedEnd));
    }
}
