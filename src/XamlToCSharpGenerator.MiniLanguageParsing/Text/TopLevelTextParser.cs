using System;
using System.Collections.Immutable;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Text;

public static class TopLevelTextParser
{
    public static ImmutableArray<TopLevelTextSegment> SplitTopLevelSegments(
        string value,
        char separator,
        bool trimTokens = false,
        bool removeEmpty = false)
    {
        if (value is null)
        {
            return ImmutableArray<TopLevelTextSegment>.Empty;
        }

        var segments = ImmutableArray.CreateBuilder<TopLevelTextSegment>();
        var start = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    break;
                default:
                    break;
            }

            if (ch == separator &&
                braceDepth == 0 &&
                bracketDepth == 0 &&
                parenthesisDepth == 0)
            {
                AddSegment(segments, value, start, index, trimTokens, removeEmpty);
                start = index + 1;
            }
        }

        AddSegment(segments, value, start, value.Length, trimTokens, removeEmpty);
        return segments.ToImmutable();
    }

    public static ImmutableArray<string> SplitTopLevel(
        string value,
        char separator,
        bool trimTokens = false,
        bool removeEmpty = false)
    {
        if (value is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var segments = SplitTopLevelSegments(value, separator, trimTokens, removeEmpty);
        if (segments.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var result = ImmutableArray.CreateBuilder<string>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            result.Add(segments[i].Text);
        }

        return result.ToImmutable();
    }

    public static int IndexOfTopLevel(string value, char token)
    {
        if (string.IsNullOrEmpty(value))
        {
            return -1;
        }

        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    continue;
                case '(':
                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    continue;
            }

            if (ch == token &&
                braceDepth == 0 &&
                bracketDepth == 0 &&
                parenthesisDepth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    public static bool TryReadBalancedContent(
        string text,
        ref int index,
        char openChar,
        char closeChar,
        out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(text) ||
            index < 0 ||
            index >= text.Length ||
            text[index] != openChar)
        {
            return false;
        }

        index++;
        var contentStart = index;
        var depth = 1;
        var inQuote = false;
        var quoteChar = '\0';

        while (index < text.Length)
        {
            var ch = text[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                index++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                index++;
                continue;
            }

            if (ch == openChar)
            {
                depth++;
                index++;
                continue;
            }

            if (ch == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    content = text.Substring(contentStart, index - contentStart);
                    index++;
                    return true;
                }

                index++;
                continue;
            }

            index++;
        }

        return false;
    }

    public static bool EqualsOrdinalIgnoreCaseAt(string text, int index, string token)
    {
        if (string.IsNullOrEmpty(text) ||
            string.IsNullOrEmpty(token) ||
            index < 0 ||
            index + token.Length > text.Length)
        {
            return false;
        }

        for (var i = 0; i < token.Length; i++)
        {
            if (char.ToUpperInvariant(text[index + i]) != char.ToUpperInvariant(token[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddSegment(
        ImmutableArray<string>.Builder target,
        string source,
        int start,
        int end,
        bool trimTokens,
        bool removeEmpty)
    {
        var length = end - start;
        if (length < 0)
        {
            return;
        }

        var token = source.Substring(start, length);
        if (trimTokens)
        {
            token = token.Trim();
        }

        if (removeEmpty && token.Length == 0)
        {
            return;
        }

        target.Add(token);
    }

    private static void AddSegment(
        ImmutableArray<TopLevelTextSegment>.Builder target,
        string source,
        int start,
        int end,
        bool trimTokens,
        bool removeEmpty)
    {
        var length = end - start;
        if (length < 0)
        {
            return;
        }

        var tokenStart = start;
        var tokenEnd = end;
        if (trimTokens)
        {
            while (tokenStart < tokenEnd && char.IsWhiteSpace(source[tokenStart]))
            {
                tokenStart++;
            }

            while (tokenEnd > tokenStart && char.IsWhiteSpace(source[tokenEnd - 1]))
            {
                tokenEnd--;
            }
        }

        if (removeEmpty && tokenEnd == tokenStart)
        {
            return;
        }

        target.Add(new TopLevelTextSegment(
            source.Substring(tokenStart, tokenEnd - tokenStart),
            tokenStart,
            tokenEnd - tokenStart));
    }
}
