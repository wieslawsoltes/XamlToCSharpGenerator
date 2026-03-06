using System;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Text;

internal static class XamlXmlSourceRangeService
{
    public static bool TryCreateAttributeNameRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var start = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var offset = TextCoordinateHelper.GetOffset(text, start);
        if (offset < 0 || offset >= text.Length)
        {
            return false;
        }

        var name = attribute.Name.ToString();
        range = new SourceRange(
            start,
            TextCoordinateHelper.GetPosition(text, Math.Min(text.Length, offset + name.Length)));
        return true;
    }

    public static bool TryCreateAttributeValueRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var startPosition = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var offset = TextCoordinateHelper.GetOffset(text, startPosition);
        if (offset < 0 || offset >= text.Length)
        {
            return false;
        }

        var equalsIndex = text.IndexOf('=', offset);
        if (equalsIndex < 0)
        {
            return false;
        }

        var quoteIndex = equalsIndex + 1;
        while (quoteIndex < text.Length && char.IsWhiteSpace(text[quoteIndex]))
        {
            quoteIndex++;
        }

        if (quoteIndex >= text.Length || (text[quoteIndex] != '"' && text[quoteIndex] != '\''))
        {
            return false;
        }

        var quote = text[quoteIndex];
        var valueStart = quoteIndex + 1;
        var valueEnd = text.IndexOf(quote, valueStart);
        if (valueEnd < valueStart)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, valueStart),
            TextCoordinateHelper.GetPosition(text, valueEnd));
        return true;
    }

    public static bool TryFindAttributeAtPosition(
        string text,
        XDocument? xmlDocument,
        SourcePosition position,
        out XElement element,
        out XAttribute attribute,
        out SourceRange attributeNameRange,
        out SourceRange attributeValueRange)
    {
        element = null!;
        attribute = null!;
        attributeNameRange = default;
        attributeValueRange = default;

        if (xmlDocument?.Root is null)
        {
            return false;
        }

        foreach (var candidateElement in xmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var candidateAttribute in candidateElement.Attributes())
            {
                if (!TryCreateAttributeNameRange(text, candidateAttribute, out var candidateNameRange) ||
                    !TryCreateAttributeValueRange(text, candidateAttribute, out var candidateValueRange))
                {
                    continue;
                }

                if (!ContainsPosition(text, candidateNameRange, position) &&
                    !ContainsPosition(text, candidateValueRange, position))
                {
                    continue;
                }

                element = candidateElement;
                attribute = candidateAttribute;
                attributeNameRange = candidateNameRange;
                attributeValueRange = candidateValueRange;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }
}
