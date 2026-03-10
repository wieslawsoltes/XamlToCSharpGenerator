using System;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Text;

internal static class XamlXmlSourceRangeService
{
    public static bool TryCreateElementNameRange(string text, XElement element, out SourceRange range)
    {
        range = default;
        if (element is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
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

        if (!TryGetElementNameBounds(text, offset, out var nameStart, out var nameLength))
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart),
            TextCoordinateHelper.GetPosition(text, nameStart + nameLength));
        return true;
    }

    public static bool TryCreateAttributeNameRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (!TryGetAttributeNameBounds(text, attribute, out var nameStart, out var nameLength))
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart),
            TextCoordinateHelper.GetPosition(text, nameStart + nameLength));
        return true;
    }

    public static bool TryCreateNamespaceDeclarationPrefixRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (!attribute.IsNamespaceDeclaration ||
            !TryGetAttributeNameBounds(text, attribute, out var nameStart, out var nameLength))
        {
            return false;
        }

        const string xmlnsPrefix = "xmlns:";
        if (nameLength <= xmlnsPrefix.Length ||
            !text.AsSpan(nameStart, Math.Min(nameLength, xmlnsPrefix.Length)).SequenceEqual(xmlnsPrefix.AsSpan()))
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart + xmlnsPrefix.Length),
            TextCoordinateHelper.GetPosition(text, nameStart + nameLength));
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

    public static bool TryFindElementNameAtPosition(
        string text,
        XDocument? xmlDocument,
        SourcePosition position,
        out XElement element,
        out SourceRange elementNameRange)
    {
        element = null!;
        elementNameRange = default;

        if (xmlDocument?.Root is null)
        {
            return false;
        }

        XElement? bestElement = null;
        SourceRange bestRange = default;
        var bestSpanLength = int.MaxValue;

        foreach (var candidateElement in xmlDocument.Root.DescendantsAndSelf())
        {
            if (!TryCreateElementNameRange(text, candidateElement, out var candidateRange) ||
                !ContainsPosition(text, candidateRange, position))
            {
                continue;
            }

            var startOffset = TextCoordinateHelper.GetOffset(text, candidateRange.Start);
            var endOffset = TextCoordinateHelper.GetOffset(text, candidateRange.End);
            var spanLength = Math.Max(0, endOffset - startOffset);
            if (spanLength >= bestSpanLength)
            {
                continue;
            }

            bestElement = candidateElement;
            bestRange = candidateRange;
            bestSpanLength = spanLength;
        }

        if (bestElement is null)
        {
            return false;
        }

        element = bestElement;
        elementNameRange = bestRange;
        return true;
    }

    public static bool TryCreateElementContentRange(
        string text,
        XElement element,
        string rawContent,
        out SourceRange range)
    {
        range = default;
        if (string.IsNullOrEmpty(text) ||
            string.IsNullOrEmpty(rawContent) ||
            element is not IXmlLineInfo lineInfo ||
            !lineInfo.HasLineInfo())
        {
            return false;
        }

        var elementStart = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var elementOffset = TextCoordinateHelper.GetOffset(text, elementStart);
        if (elementOffset < 0 || elementOffset >= text.Length)
        {
            return false;
        }

        var startTagEnd = FindElementStartTagEnd(text, elementOffset);
        if (startTagEnd < 0 || startTagEnd >= text.Length)
        {
            return false;
        }

        var closingTagStart = FindClosingTagStart(text, startTagEnd + 1, element.Name.LocalName);
        if (closingTagStart < 0 || closingTagStart < startTagEnd + 1)
        {
            return false;
        }

        var candidateRange = text.AsSpan(startTagEnd + 1, closingTagStart - startTagEnd - 1);
        var relativeContentStart = candidateRange.IndexOf(rawContent.AsSpan(), StringComparison.Ordinal);
        if (relativeContentStart < 0)
        {
            return false;
        }

        var contentStart = startTagEnd + 1 + relativeContentStart;
        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, contentStart),
            TextCoordinateHelper.GetPosition(text, contentStart + rawContent.Length));
        return true;
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static bool TryGetAttributeNameBounds(string text, XAttribute attribute, out int nameStart, out int nameLength)
    {
        nameStart = 0;
        nameLength = 0;
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

        var cursor = offset;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        var end = cursor;
        while (end < text.Length &&
               !char.IsWhiteSpace(text[end]) &&
               text[end] != '=' &&
               text[end] != '>' &&
               text[end] != '/')
        {
            end++;
        }

        if (end <= cursor)
        {
            return false;
        }

        nameStart = cursor;
        nameLength = end - cursor;
        return true;
    }

    private static bool TryGetElementNameBounds(string text, int offset, out int nameStart, out int nameLength)
    {
        nameStart = 0;
        nameLength = 0;

        var cursor = offset;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        if (cursor < text.Length && text[cursor] == '<')
        {
            cursor++;
        }

        if (cursor < text.Length && text[cursor] == '/')
        {
            cursor++;
        }

        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        var end = cursor;
        while (end < text.Length &&
               !char.IsWhiteSpace(text[end]) &&
               text[end] != '>' &&
               text[end] != '/')
        {
            end++;
        }

        if (end <= cursor)
        {
            return false;
        }

        nameStart = cursor;
        nameLength = end - cursor;
        return true;
    }

    private static int FindElementStartTagEnd(string text, int startOffset)
    {
        var inQuotes = false;
        var quote = '\0';
        for (var index = startOffset; index < text.Length; index++)
        {
            var current = text[index];
            if (inQuotes)
            {
                if (current == quote)
                {
                    inQuotes = false;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                inQuotes = true;
                quote = current;
                continue;
            }

            if (current == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindClosingTagStart(string text, int searchStart, string localName)
    {
        for (var index = text.IndexOf("</", searchStart, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf("</", index + 2, StringComparison.Ordinal))
        {
            var nameStart = index + 2;
            while (nameStart < text.Length && char.IsWhiteSpace(text[nameStart]))
            {
                nameStart++;
            }

            var cursor = nameStart;
            while (cursor < text.Length &&
                   !char.IsWhiteSpace(text[cursor]) &&
                   text[cursor] != '>')
            {
                cursor++;
            }

            if (cursor <= nameStart)
            {
                continue;
            }

            var qualifiedName = text.Substring(nameStart, cursor - nameStart);
            var separator = qualifiedName.IndexOf(':');
            var candidateLocalName = separator >= 0 && separator < qualifiedName.Length - 1
                ? qualifiedName.Substring(separator + 1)
                : qualifiedName;
            if (string.Equals(candidateLocalName, localName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
