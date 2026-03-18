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

    public static bool TryCreateAttributeRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (!TryGetAttributeNameBounds(text, attribute, out var nameStart, out _) ||
            !TryCreateAttributeValueRange(text, attribute, out var valueRange))
        {
            return false;
        }

        var valueEndOffset = TextCoordinateHelper.GetOffset(text, valueRange.End);
        if (valueEndOffset < 0 || valueEndOffset >= text.Length)
        {
            return false;
        }

        var closingQuoteOffset = valueEndOffset;
        while (closingQuoteOffset < text.Length && text[closingQuoteOffset] is not '"' and not '\'')
        {
            closingQuoteOffset++;
        }

        if (closingQuoteOffset >= text.Length)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart),
            TextCoordinateHelper.GetPosition(text, closingQuoteOffset + 1));
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

    public static bool TryCreateElementRange(string text, XElement element, out SourceRange range)
    {
        range = default;
        if (string.IsNullOrEmpty(text) ||
            element is not IXmlLineInfo lineInfo ||
            !lineInfo.HasLineInfo())
        {
            return false;
        }

        var start = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var startOffset = NormalizeElementStartOffset(text, TextCoordinateHelper.GetOffset(text, start));
        if (startOffset < 0 || startOffset >= text.Length)
        {
            return false;
        }

        if (!TryFindElementBounds(text, startOffset, out _, out var endOffset))
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset),
            TextCoordinateHelper.GetPosition(text, endOffset));
        return true;
    }

    public static bool TryCreateClosingElementNameRange(string text, XElement element, out SourceRange range)
    {
        range = default;
        if (string.IsNullOrEmpty(text) ||
            element is not IXmlLineInfo lineInfo ||
            !lineInfo.HasLineInfo())
        {
            return false;
        }

        var start = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var startOffset = NormalizeElementStartOffset(text, TextCoordinateHelper.GetOffset(text, start));
        if (startOffset < 0 || startOffset >= text.Length ||
            !TryFindElementBounds(text, startOffset, out var startTagEnd, out var elementEnd))
        {
            return false;
        }

        var closingTagStart = FindClosingTagStart(text, startTagEnd + 1, elementEnd);
        if (closingTagStart < 0)
        {
            return false;
        }

        var nameStart = closingTagStart + 2;
        while (nameStart < elementEnd && char.IsWhiteSpace(text[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < elementEnd &&
               !char.IsWhiteSpace(text[nameEnd]) &&
               text[nameEnd] != '>')
        {
            nameEnd++;
        }

        if (nameEnd <= nameStart)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart),
            TextCoordinateHelper.GetPosition(text, nameEnd));
        return true;
    }

    public static bool TryFindInnermostElementAtPosition(
        string text,
        XDocument? xmlDocument,
        SourcePosition position,
        out XElement element,
        out SourceRange elementRange)
    {
        element = null!;
        elementRange = default;
        if (xmlDocument?.Root is null)
        {
            return false;
        }

        XElement? bestElement = null;
        SourceRange bestRange = default;
        var bestSpanLength = int.MaxValue;

        foreach (var candidateElement in xmlDocument.Root.DescendantsAndSelf())
        {
            if (!TryCreateElementRange(text, candidateElement, out var candidateRange) ||
                !ContainsPosition(text, candidateRange, position))
            {
                continue;
            }

            var spanLength = TextCoordinateHelper.GetOffset(text, candidateRange.End) -
                             TextCoordinateHelper.GetOffset(text, candidateRange.Start);
            if (spanLength < 0 || spanLength >= bestSpanLength)
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
        elementRange = bestRange;
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
        var elementOffset = NormalizeElementStartOffset(text, TextCoordinateHelper.GetOffset(text, elementStart));
        if (elementOffset < 0 || elementOffset >= text.Length)
        {
            return false;
        }

        if (!TryFindElementBounds(text, elementOffset, out var startTagEnd, out var elementEnd))
        {
            return false;
        }

        if (startTagEnd < 0 || startTagEnd >= text.Length)
        {
            return false;
        }

        var closingTagStart = FindClosingTagStart(text, startTagEnd + 1, elementEnd);
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

    private static bool TryFindElementBounds(string text, int startOffset, out int startTagEnd, out int elementEnd)
    {
        startTagEnd = FindElementStartTagEnd(text, startOffset);
        elementEnd = -1;
        if (startTagEnd < 0)
        {
            return false;
        }

        if (startTagEnd > startOffset && text[startTagEnd - 1] == '/')
        {
            elementEnd = startTagEnd + 1;
            return true;
        }

        if (!TryGetElementNameBounds(text, startOffset, out var nameStart, out var nameLength))
        {
            return false;
        }

        var qualifiedName = text.Substring(nameStart, nameLength);
        var depth = 1;
        for (var index = startTagEnd + 1; index < text.Length; index++)
        {
            if (text[index] != '<')
            {
                continue;
            }

            if (StartsWith(text, index, "<!--"))
            {
                var commentEnd = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    break;
                }

                index = commentEnd + 2;
                continue;
            }

            if (StartsWith(text, index, "<![CDATA["))
            {
                var cdataEnd = text.IndexOf("]]>", index + 9, StringComparison.Ordinal);
                if (cdataEnd < 0)
                {
                    break;
                }

                index = cdataEnd + 2;
                continue;
            }

            if (StartsWith(text, index, "<?"))
            {
                var processingEnd = text.IndexOf("?>", index + 2, StringComparison.Ordinal);
                if (processingEnd < 0)
                {
                    break;
                }

                index = processingEnd + 1;
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '!')
            {
                var declarationEnd = FindElementStartTagEnd(text, index);
                if (declarationEnd < 0)
                {
                    break;
                }

                index = declarationEnd;
                continue;
            }

            var isClosingTag = index + 1 < text.Length && text[index + 1] == '/';
            var tokenStart = isClosingTag ? index + 2 : index + 1;
            if (!TryGetElementNameBounds(text, tokenStart, out var candidateNameStart, out var candidateNameLength))
            {
                continue;
            }

            var candidateName = text.Substring(candidateNameStart, candidateNameLength);
            var candidateTagEnd = FindElementStartTagEnd(text, tokenStart);
            if (candidateTagEnd < 0)
            {
                break;
            }

            if (!string.Equals(candidateName, qualifiedName, StringComparison.Ordinal))
            {
                index = candidateTagEnd;
                continue;
            }

            if (isClosingTag)
            {
                depth--;
                if (depth == 0)
                {
                    elementEnd = candidateTagEnd + 1;
                    return true;
                }

                index = candidateTagEnd;
                continue;
            }

            if (candidateTagEnd > index && text[candidateTagEnd - 1] != '/')
            {
                depth++;
            }

            index = candidateTagEnd;
        }

        return false;
    }

    private static int FindClosingTagStart(string text, int searchStart, int elementEnd)
    {
        for (var index = searchStart; index < elementEnd - 1; index++)
        {
            if (text[index] == '<' && text[index + 1] == '/')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool StartsWith(string text, int index, string value)
    {
        return index + value.Length <= text.Length &&
               string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
    }

    private static int NormalizeElementStartOffset(string text, int offset)
    {
        if (offset <= 0 || offset >= text.Length || text[offset] == '<')
        {
            return offset;
        }

        for (var index = offset; index >= 0; index--)
        {
            var current = text[index];
            if (current == '<')
            {
                return index;
            }

            if (current is '\r' or '\n')
            {
                break;
            }
        }

        return offset;
    }
}
