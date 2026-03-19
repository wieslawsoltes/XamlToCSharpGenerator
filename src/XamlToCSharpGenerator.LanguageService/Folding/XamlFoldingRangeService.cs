using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Folding;

internal sealed class XamlFoldingRangeService
{
    public ImmutableArray<XamlFoldingRange> GetFoldingRanges(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ImmutableArray<XamlFoldingRange>.Empty;
        }

        var sourceText = SourceText.From(text);
        var builder = ImmutableArray.CreateBuilder<XamlFoldingRange>();
        var elementStack = new List<ElementFrame>();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] != '<')
            {
                index++;
                continue;
            }

            if (StartsWith(text, index, "<!--"))
            {
                var end = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                AddRange(builder, sourceText, index, end + 2, kind: "comment");
                index = end + 3;
                continue;
            }

            if (StartsWith(text, index, "<![CDATA["))
            {
                var end = text.IndexOf("]]>", index + 9, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                AddRange(builder, sourceText, index, end + 2, kind: "region");
                index = end + 3;
                continue;
            }

            if (StartsWith(text, index, "<?"))
            {
                var end = text.IndexOf("?>", index + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                AddRange(builder, sourceText, index, end + 1, kind: "region");
                index = end + 2;
                continue;
            }

            if (StartsWith(text, index, "</"))
            {
                var closingTagName = ParseTagName(text, index + 2, out var nameEnd);
                if (string.IsNullOrWhiteSpace(closingTagName))
                {
                    index++;
                    continue;
                }

                var closingBracket = FindTagEnd(text, nameEnd);
                if (closingBracket < 0)
                {
                    break;
                }

                for (var stackIndex = elementStack.Count - 1; stackIndex >= 0; stackIndex--)
                {
                    if (!string.Equals(elementStack[stackIndex].Name, closingTagName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var frame = elementStack[stackIndex];
                    elementStack.RemoveRange(stackIndex, elementStack.Count - stackIndex);
                    AddRange(builder, sourceText, frame.StartOffset, closingBracket, kind: "region");
                    break;
                }

                index = closingBracket + 1;
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '!')
            {
                var declarationEnd = FindTagEnd(text, index + 2);
                if (declarationEnd < 0)
                {
                    break;
                }

                AddRange(builder, sourceText, index, declarationEnd, kind: "region");
                index = declarationEnd + 1;
                continue;
            }

            var tagName = ParseTagName(text, index + 1, out var openNameEnd);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                index++;
                continue;
            }

            var openTagEnd = FindTagEnd(text, openNameEnd);
            if (openTagEnd < 0)
            {
                break;
            }

            AddRange(builder, sourceText, index, openTagEnd, kind: "region");
            if (!IsSelfClosingTag(text, index, openTagEnd))
            {
                elementStack.Add(new ElementFrame(tagName, index));
            }

            index = openTagEnd + 1;
        }

        if (builder.Count == 0)
        {
            return ImmutableArray<XamlFoldingRange>.Empty;
        }

        return builder
            .ToImmutable()
            .Distinct()
            .OrderBy(static range => range.StartLine)
            .ThenBy(static range => range.EndLine)
            .ToImmutableArray();
    }

    private static bool StartsWith(string text, int startIndex, string value)
    {
        return startIndex >= 0 &&
               startIndex + value.Length <= text.Length &&
               string.CompareOrdinal(text, startIndex, value, 0, value.Length) == 0;
    }

    private static string ParseTagName(string text, int startIndex, out int endIndex)
    {
        var index = startIndex;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        var nameStart = index;
        while (index < text.Length)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current) || current is '/' or '>' or '=')
            {
                break;
            }

            index++;
        }

        endIndex = index;
        return index > nameStart ? text.Substring(nameStart, index - nameStart) : string.Empty;
    }

    private static int FindTagEnd(string text, int startIndex)
    {
        var quote = '\0';
        for (var index = startIndex; index < text.Length; index++)
        {
            var current = text[index];
            if (quote == '\0')
            {
                if (current is '"' or '\'')
                {
                    quote = current;
                    continue;
                }

                if (current == '>')
                {
                    return index;
                }

                continue;
            }

            if (current == quote)
            {
                quote = '\0';
            }
        }

        return -1;
    }

    private static bool IsSelfClosingTag(string text, int tagStart, int tagEnd)
    {
        for (var index = tagEnd - 1; index > tagStart; index--)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current))
            {
                continue;
            }

            return current == '/';
        }

        return false;
    }

    private static void AddRange(
        ImmutableArray<XamlFoldingRange>.Builder builder,
        SourceText sourceText,
        int startOffset,
        int endOffset,
        string kind)
    {
        if (endOffset <= startOffset)
        {
            return;
        }

        var start = sourceText.Lines.GetLinePosition(startOffset);
        var end = sourceText.Lines.GetLinePosition(Math.Min(endOffset, sourceText.Length));
        if (end.Line <= start.Line)
        {
            return;
        }

        builder.Add(new XamlFoldingRange(start.Line, end.Line, kind));
    }

    private readonly record struct ElementFrame(string Name, int StartOffset);
}
