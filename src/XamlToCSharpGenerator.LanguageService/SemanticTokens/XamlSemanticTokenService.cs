using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.SemanticTokens;

public sealed class XamlSemanticTokenService
{
    public static readonly ImmutableArray<string> TokenTypes =
    [
        "xamlDelimiter",
        "xamlName",
        "xamlAttribute",
        "xamlAttributeValue",
        "xamlAttributeQuotes",
        "xamlComment",
        "xamlKeyword",
        "xamlMarkupExtensionClass",
        "xamlMarkupExtensionParameterName",
        "xamlMarkupExtensionParameterValue",
        "xamlNamespacePrefix",
        "xamlText",
        "number"
    ];

    public ImmutableArray<XamlSemanticToken> GetTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ImmutableArray<XamlSemanticToken>.Empty;
        }

        var tokens = ImmutableArray.CreateBuilder<XamlSemanticToken>();
        var mapper = new OffsetLineMap(text);

        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '<')
            {
                TokenizeTag(text, ref index, mapper, tokens);
                continue;
            }

            TokenizeTextNode(text, ref index, mapper, tokens);
        }

        return tokens.ToImmutable();
    }

    private static void TokenizeTextNode(
        string text,
        ref int index,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var end = text.IndexOf('<', index);
        if (end < 0)
        {
            end = text.Length;
        }

        var cursor = index;
        while (cursor < end)
        {
            while (cursor < end && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            var start = cursor;
            while (cursor < end && !char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            if (cursor > start)
            {
                AddToken(tokens, mapper, start, cursor - start, "xamlText");
            }
        }

        index = end;
    }

    private static void TokenizeTag(
        string text,
        ref int index,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        if (index + 3 < text.Length &&
            text[index + 1] == '!' &&
            text[index + 2] == '-' &&
            text[index + 3] == '-')
        {
            AddToken(tokens, mapper, index, 4, "xamlDelimiter");
            var commentEnd = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
            if (commentEnd < 0)
            {
                AddToken(tokens, mapper, index + 4, text.Length - index - 4, "xamlComment");
                index = text.Length;
                return;
            }

            if (commentEnd > index + 4)
            {
                AddToken(tokens, mapper, index + 4, commentEnd - index - 4, "xamlComment");
            }

            AddToken(tokens, mapper, commentEnd, 3, "xamlDelimiter");
            index = commentEnd + 3;
            return;
        }

        if (index + 1 < text.Length && text[index + 1] == '?')
        {
            AddToken(tokens, mapper, index, 2, "xamlDelimiter");
            var piStart = index + 2;
            var piLength = ReadIdentifierLength(text, piStart);
            if (piLength > 0)
            {
                AddToken(tokens, mapper, piStart, piLength, "xamlKeyword");
            }

            var piEnd = text.IndexOf("?>", index + 2, StringComparison.Ordinal);
            if (piEnd >= 0)
            {
                AddToken(tokens, mapper, piEnd, 2, "xamlDelimiter");
                index = piEnd + 2;
            }
            else
            {
                index = text.Length;
            }

            return;
        }

        AddToken(tokens, mapper, index, 1, "xamlDelimiter");
        index++;
        if (index < text.Length && text[index] == '/')
        {
            AddToken(tokens, mapper, index, 1, "xamlDelimiter");
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        var elementNameStart = index;
        var elementNameLength = ReadIdentifierLength(text, elementNameStart);
        if (elementNameLength > 0)
        {
            AddQualifiedNameTokens(tokens, mapper, elementNameStart, elementNameLength, "xamlName");
            index += elementNameLength;
        }

        while (index < text.Length)
        {
            if (text[index] == '>')
            {
                AddToken(tokens, mapper, index, 1, "xamlDelimiter");
                index++;
                break;
            }

            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '>')
            {
                AddToken(tokens, mapper, index, 2, "xamlDelimiter");
                index += 2;
                break;
            }

            if (text[index] == '"' || text[index] == '\'')
            {
                TokenizeQuotedValue(text, ref index, mapper, tokens);
                continue;
            }

            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            var attributeStart = index;
            var attributeLength = ReadIdentifierLength(text, attributeStart);
            if (attributeLength <= 0)
            {
                index++;
                continue;
            }

            TokenizeAttributeName(text, attributeStart, attributeLength, mapper, tokens);
            index += attributeLength;

            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index < text.Length && text[index] == '=')
            {
                AddToken(tokens, mapper, index, 1, "xamlDelimiter");
                index++;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index < text.Length && (text[index] == '"' || text[index] == '\''))
                {
                    TokenizeQuotedValue(text, ref index, mapper, tokens);
                }
                else
                {
                    var valueStart = index;
                    while (index < text.Length &&
                           !char.IsWhiteSpace(text[index]) &&
                           text[index] is not '>' and not '/')
                    {
                        index++;
                    }

                    var valueLength = index - valueStart;
                    if (valueLength > 0)
                    {
                        TokenizeAttributeValueSpan(text, valueStart, valueLength, mapper, tokens);
                    }
                }
            }
        }
    }

    private static void TokenizeQuotedValue(
        string text,
        ref int index,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var quote = text[index];
        AddToken(tokens, mapper, index, 1, "xamlAttributeQuotes");
        var valueStart = index + 1;
        var valueEnd = text.IndexOf(quote, valueStart);
        if (valueEnd < 0)
        {
            valueEnd = text.Length;
        }

        if (valueEnd > valueStart)
        {
            TokenizeAttributeValueSpan(text, valueStart, valueEnd - valueStart, mapper, tokens);
        }

        if (valueEnd < text.Length)
        {
            AddToken(tokens, mapper, valueEnd, 1, "xamlAttributeQuotes");
        }

        index = valueEnd < text.Length ? valueEnd + 1 : valueEnd;
    }

    private static void TokenizeAttributeName(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var name = text.Substring(start, length);
        if (string.Equals(name, "xmlns", StringComparison.Ordinal))
        {
            AddToken(tokens, mapper, start, length, "xamlKeyword");
            return;
        }

        if (name.StartsWith("xmlns:", StringComparison.Ordinal))
        {
            AddToken(tokens, mapper, start, "xmlns".Length, "xamlKeyword");
            AddToken(tokens, mapper, start + "xmlns".Length, 1, "xamlDelimiter");
            var namespacePrefixStart = start + "xmlns:".Length;
            var namespacePrefixLength = length - "xmlns:".Length;
            if (namespacePrefixLength > 0)
            {
                AddToken(tokens, mapper, namespacePrefixStart, namespacePrefixLength, "xamlNamespacePrefix");
            }

            return;
        }

        var dotIndex = name.IndexOf('.');
        if (dotIndex > 0)
        {
            var ownerTokenStart = start;
            var ownerTokenLength = dotIndex;
            AddQualifiedNameTokens(tokens, mapper, ownerTokenStart, ownerTokenLength, "xamlName");
            AddToken(tokens, mapper, start + dotIndex, 1, "xamlDelimiter");

            var propertyStart = start + dotIndex + 1;
            var propertyLength = length - dotIndex - 1;
            if (propertyLength > 0)
            {
                AddToken(tokens, mapper, propertyStart, propertyLength, "xamlAttribute");
            }

            return;
        }

        AddQualifiedNameTokens(tokens, mapper, start, length, "xamlAttribute");
    }

    private static void TokenizeAttributeValueSpan(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        if (length <= 0)
        {
            return;
        }

        if (IsNumericToken(text, start, length))
        {
            AddToken(tokens, mapper, start, length, "number");
            return;
        }

        if (length >= 2 && text[start] == '{' && text[start + length - 1] == '}')
        {
            TokenizeMarkupExtension(text, start, length, mapper, tokens);
            return;
        }

        AddQualifiedNameTokens(tokens, mapper, start, length, "xamlAttributeValue");
    }

    private static void TokenizeMarkupExtension(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var end = start + length;
        AddToken(tokens, mapper, start, 1, "xamlDelimiter");
        if (length > 1)
        {
            AddToken(tokens, mapper, end - 1, 1, "xamlDelimiter");
        }

        var contentStart = start + 1;
        var contentEnd = end - 1;
        TrimRange(text, ref contentStart, ref contentEnd);
        if (contentStart >= contentEnd)
        {
            return;
        }

        var classLength = ReadMarkupExtensionIdentifierLength(text, contentStart, contentEnd);
        if (classLength > 0)
        {
            AddQualifiedNameTokens(tokens, mapper, contentStart, classLength, "xamlMarkupExtensionClass");
            contentStart += classLength;
        }

        while (contentStart < contentEnd)
        {
            while (contentStart < contentEnd && char.IsWhiteSpace(text[contentStart]))
            {
                contentStart++;
            }

            if (contentStart >= contentEnd)
            {
                break;
            }

            if (text[contentStart] == ',')
            {
                AddToken(tokens, mapper, contentStart, 1, "xamlDelimiter");
                contentStart++;
                continue;
            }

            var segmentStart = contentStart;
            var depth = 0;
            while (contentStart < contentEnd)
            {
                var current = text[contentStart];
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
                else if (current == ',' && depth == 0)
                {
                    break;
                }

                contentStart++;
            }

            var segmentEnd = contentStart;
            TrimRange(text, ref segmentStart, ref segmentEnd);
            if (segmentStart >= segmentEnd)
            {
                continue;
            }

            var equalsIndex = IndexOf(text, segmentStart, segmentEnd, '=');
            if (equalsIndex > segmentStart && equalsIndex < segmentEnd - 1)
            {
                AddQualifiedNameTokens(
                    tokens,
                    mapper,
                    segmentStart,
                    equalsIndex - segmentStart,
                    "xamlMarkupExtensionParameterName");
                AddToken(tokens, mapper, equalsIndex, 1, "xamlDelimiter");

                var valueStart = equalsIndex + 1;
                var valueEnd = segmentEnd;
                TrimRange(text, ref valueStart, ref valueEnd);
                if (valueStart < valueEnd)
                {
                    TokenizeMarkupExtensionParameterValue(text, valueStart, valueEnd - valueStart, mapper, tokens);
                }
            }
            else
            {
                TokenizeMarkupExtensionParameterValue(text, segmentStart, segmentEnd - segmentStart, mapper, tokens);
            }
        }
    }

    private static void TokenizeMarkupExtensionParameterValue(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        if (length <= 0)
        {
            return;
        }

        if (IsNumericToken(text, start, length))
        {
            AddToken(tokens, mapper, start, length, "number");
            return;
        }

        AddQualifiedNameTokens(tokens, mapper, start, length, "xamlMarkupExtensionParameterValue");
    }

    private static void AddQualifiedNameTokens(
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        OffsetLineMap mapper,
        int start,
        int length,
        string defaultTokenType)
    {
        var text = mapper.Text;
        var separatorIndex = text.AsSpan(start, length).IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= length - 1)
        {
            AddToken(tokens, mapper, start, length, defaultTokenType);
            return;
        }

        AddToken(tokens, mapper, start, separatorIndex, "xamlNamespacePrefix");
        AddToken(tokens, mapper, start + separatorIndex, 1, "xamlDelimiter");
        AddToken(tokens, mapper, start + separatorIndex + 1, length - separatorIndex - 1, defaultTokenType);
    }

    private static void AddToken(
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        OffsetLineMap mapper,
        int start,
        int length,
        string tokenType)
    {
        if (start < 0 || length <= 0 || start + length > mapper.Text.Length)
        {
            return;
        }

        var position = mapper.GetPosition(start);
        tokens.Add(new XamlSemanticToken(position.Line, position.Character, length, tokenType));
    }

    private static bool IsNumericToken(string text, int start, int length)
    {
        var span = text.AsSpan(start, length);
        var hasDigit = false;
        for (var index = 0; index < span.Length; index++)
        {
            var value = span[index];
            if (char.IsDigit(value))
            {
                hasDigit = true;
                continue;
            }

            if (value is '+' or '-' or '.' or ',' or '%' or 'e' or 'E')
            {
                continue;
            }

            return false;
        }

        return hasDigit;
    }

    private static int ReadMarkupExtensionIdentifierLength(string text, int start, int endExclusive)
    {
        if (start < 0 || start >= endExclusive || start >= text.Length)
        {
            return 0;
        }

        var index = start;
        while (index < endExclusive && index < text.Length)
        {
            var ch = text[index];
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.' or '-'))
            {
                break;
            }

            index++;
        }

        return index - start;
    }

    private static int IndexOf(string text, int start, int endExclusive, char value)
    {
        for (var index = start; index < endExclusive; index++)
        {
            if (text[index] == value)
            {
                return index;
            }
        }

        return -1;
    }

    private static void TrimRange(string text, ref int start, ref int endExclusive)
    {
        while (start < endExclusive && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (endExclusive > start && char.IsWhiteSpace(text[endExclusive - 1]))
        {
            endExclusive--;
        }
    }

    private static int ReadIdentifierLength(string text, int start)
    {
        if (start < 0 || start >= text.Length)
        {
            return 0;
        }

        var length = 0;
        var index = start;
        while (index < text.Length)
        {
            var ch = text[index];
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.' or '-'))
            {
                break;
            }

            length++;
            index++;
        }

        return length;
    }

    private sealed class OffsetLineMap
    {
        private readonly ImmutableArray<int> _lineStartOffsets;

        public OffsetLineMap(string text)
        {
            Text = text;

            var starts = ImmutableArray.CreateBuilder<int>();
            starts.Add(0);
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    starts.Add(index + 1);
                }
            }

            _lineStartOffsets = starts.ToImmutable();
        }

        public string Text { get; }

        public (int Line, int Character) GetPosition(int offset)
        {
            if (offset <= 0)
            {
                return (0, 0);
            }

            var boundedOffset = Math.Min(offset, Text.Length);
            var lineIndex = _lineStartOffsets.BinarySearch(boundedOffset);
            if (lineIndex < 0)
            {
                lineIndex = ~lineIndex - 1;
            }

            var lineStart = _lineStartOffsets[Math.Max(0, lineIndex)];
            return (lineIndex, boundedOffset - lineStart);
        }
    }
}
