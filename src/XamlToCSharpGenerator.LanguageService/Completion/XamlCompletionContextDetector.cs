using System;
using System.Text;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal static class XamlCompletionContextDetector
{
    public static XamlCompletionContext Detect(string text, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        if (offset < 0 || offset > text.Length)
        {
            return new XamlCompletionContext(XamlCompletionContextKind.Unknown, string.Empty, null, null, null, offset, offset);
        }

        if (!TryFindCurrentTagStart(text, offset, out var tagStart))
        {
            return new XamlCompletionContext(XamlCompletionContextKind.Unknown, string.Empty, null, null, null, offset, offset);
        }

        var fragmentEnd = offset;
        var fragmentLength = Math.Max(0, fragmentEnd - tagStart - 1);
        var fragment = fragmentLength == 0 ? string.Empty : text.Substring(tagStart + 1, fragmentLength);
        if (fragment.StartsWith("!", StringComparison.Ordinal))
        {
            return new XamlCompletionContext(XamlCompletionContextKind.Unknown, string.Empty, null, null, null, offset, offset);
        }

        var inQuotes = IsInsideAttributeQuotes(fragment);
        var currentElement = GetCurrentElementName(fragment);

        if (inQuotes)
        {
            var (attributeName, attributeValue) = TryGetCurrentAttribute(fragment);
            var tokenSpan = FindTokenSpan(text, offset, allowSpecialMarkupCharacters: true);
            var token = tokenSpan.Length > 0
                ? text.Substring(tokenSpan.Start, tokenSpan.Length)
                : string.Empty;

            var kind = token.StartsWith("{", StringComparison.Ordinal)
                ? XamlCompletionContextKind.MarkupExtension
                : XamlCompletionContextKind.AttributeValue;

            return new XamlCompletionContext(
                kind,
                token,
                currentElement,
                attributeName,
                attributeValue,
                tokenSpan.Start,
                tokenSpan.Start + tokenSpan.Length);
        }

        var hasWhitespace = ContainsWhitespace(fragment);
        var tokenRange = FindTokenSpan(text, offset, allowSpecialMarkupCharacters: false);
        var rawToken = tokenRange.Length > 0
            ? text.Substring(tokenRange.Start, tokenRange.Length)
            : string.Empty;

        if (!hasWhitespace)
        {
            var kind = XamlPropertyElementSemantics.TrySplitOwnerQualifiedPropertyFragment(rawToken, out _, out _)
                ? XamlCompletionContextKind.QualifiedPropertyElement
                : XamlCompletionContextKind.ElementName;

            return new XamlCompletionContext(
                kind,
                rawToken,
                currentElement,
                null,
                null,
                tokenRange.Start,
                tokenRange.Start + tokenRange.Length);
        }

        var currentAttributeName = TryGetCurrentAttributeName(fragment);

        return new XamlCompletionContext(
            XamlCompletionContextKind.AttributeName,
            rawToken,
            currentElement,
            currentAttributeName,
            null,
            tokenRange.Start,
            tokenRange.Start + tokenRange.Length);
    }

    private static bool ContainsWhitespace(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideAttributeQuotes(string fragment)
    {
        var quoteCount = 0;
        foreach (var ch in fragment)
        {
            if (ch == '"')
            {
                quoteCount++;
            }
        }

        return quoteCount % 2 == 1;
    }

    private static string? GetCurrentElementName(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return null;
        }

        var trimmed = fragment.TrimStart();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1);
        }

        var builder = new StringBuilder();
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch) || ch == '/' || ch == '>')
            {
                break;
            }

            builder.Append(ch);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string? TryGetCurrentAttributeName(string fragment)
    {
        var (attributeName, _) = TryGetCurrentAttribute(fragment);
        return attributeName;
    }

    private static (string? AttributeName, string? AttributeValue) TryGetCurrentAttribute(string fragment)
    {
        var quoteIndex = fragment.LastIndexOf('"');
        if (quoteIndex < 0)
        {
            return (null, null);
        }

        var previousEqualsIndex = fragment.LastIndexOf('=', quoteIndex);
        if (previousEqualsIndex < 0)
        {
            return (null, null);
        }

        var nameEnd = previousEqualsIndex - 1;
        while (nameEnd >= 0 && char.IsWhiteSpace(fragment[nameEnd]))
        {
            nameEnd--;
        }

        if (nameEnd < 0)
        {
            return (null, null);
        }

        var nameStart = nameEnd;
        while (nameStart >= 0 && !char.IsWhiteSpace(fragment[nameStart]))
        {
            nameStart--;
        }

        nameStart++;
        var name = fragment.Substring(nameStart, nameEnd - nameStart + 1);

        var valueStart = previousEqualsIndex + 1;
        while (valueStart < fragment.Length && (char.IsWhiteSpace(fragment[valueStart]) || fragment[valueStart] == '"'))
        {
            valueStart++;
        }

        var value = valueStart <= fragment.Length
            ? fragment.Substring(valueStart)
            : string.Empty;

        return (name, value);
    }

    private static (int Start, int Length) FindTokenSpan(string text, int offset, bool allowSpecialMarkupCharacters)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var start = Math.Min(offset, text.Length);
        while (start > 0 && IsTokenCharacter(text[start - 1], allowSpecialMarkupCharacters))
        {
            start--;
        }

        var end = Math.Min(offset, text.Length);
        while (end < text.Length && IsTokenCharacter(text[end], allowSpecialMarkupCharacters))
        {
            end++;
        }

        return (start, Math.Max(0, end - start));
    }

    private static bool IsTokenCharacter(char value, bool allowSpecialMarkupCharacters)
    {
        if (char.IsLetterOrDigit(value) || value is '_' or ':' or '.' or '-')
        {
            return true;
        }

        if (!allowSpecialMarkupCharacters)
        {
            return false;
        }

        return value is '{' or '}' or ',' or '=' or '(' or ')' or '+' or '*' or '/';
    }

    private static bool TryFindCurrentTagStart(string text, int offset, out int tagStart)
    {
        tagStart = -1;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var scanEnd = Math.Min(Math.Max(0, offset), text.Length);
        var lastOpenTag = -1;
        var lastClosedTag = -1;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < scanEnd; index++)
        {
            var ch = text[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            if (ch == '<')
            {
                lastOpenTag = index;
                continue;
            }

            if (ch == '>')
            {
                lastClosedTag = index;
            }
        }

        if (lastOpenTag < 0 || lastOpenTag < lastClosedTag)
        {
            return false;
        }

        tagStart = lastOpenTag;
        return true;
    }
}
