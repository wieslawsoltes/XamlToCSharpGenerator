using System.Collections.Immutable;
using System.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Text;

public static class MarkupExtensionTextParser
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
        var escaped = false;

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
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
        var escaped = false;

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
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

    public static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                builder.Append(current);
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            builder.Append(current);
        }

        if (escaped)
        {
            builder.Append('\\');
        }

        return builder.ToString();
    }

    private static void AddSegment(
        ImmutableArray<TopLevelTextSegment>.Builder segments,
        string source,
        int start,
        int end,
        bool trimTokens,
        bool removeEmpty)
    {
        if (end < start)
        {
            return;
        }

        var segmentStart = start;
        var segmentLength = end - start;
        if (trimTokens)
        {
            while (segmentLength > 0 && char.IsWhiteSpace(source[segmentStart]))
            {
                segmentStart++;
                segmentLength--;
            }

            while (segmentLength > 0 && char.IsWhiteSpace(source[segmentStart + segmentLength - 1]))
            {
                segmentLength--;
            }
        }

        if (removeEmpty && segmentLength == 0)
        {
            return;
        }

        var text = segmentLength == 0
            ? string.Empty
            : source.Substring(segmentStart, segmentLength);
        segments.Add(new TopLevelTextSegment(text, segmentStart, segmentLength));
    }
}
