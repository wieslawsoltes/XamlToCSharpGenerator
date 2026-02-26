using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlDelimitedValueSemantics
{
    public static ImmutableArray<string> SplitEnumFlagTokens(string value)
    {
        return SplitTopLevelTokens(value, [',', '|'], trimEntries: true, removeEmptyEntries: true);
    }

    public static ImmutableArray<string> SplitCollectionItems(
        string value,
        IReadOnlyList<string> separators,
        StringSplitOptions splitOptions)
    {
        if (value is null || value.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var trimEntries = (splitOptions & StringSplitOptions.TrimEntries) != 0;
        var removeEmptyEntries = (splitOptions & StringSplitOptions.RemoveEmptyEntries) != 0;

        if (separators is null || separators.Count == 0)
        {
            return SplitTopLevelTokens(
                value,
                [','],
                trimEntries: trimEntries,
                removeEmptyEntries: removeEmptyEntries);
        }

        var useTopLevelCommaSplit = separators.Count == 1 &&
                                    separators[0].Length == 1 &&
                                    separators[0][0] == ',';
        if (useTopLevelCommaSplit)
        {
            return SplitTopLevelTokens(
                value,
                [','],
                trimEntries: trimEntries,
                removeEmptyEntries: removeEmptyEntries);
        }

        var effectiveSplitOptions = splitOptions & ~StringSplitOptions.TrimEntries;
        var tokens = value.Split(separators.ToArray(), effectiveSplitOptions);
        if (tokens.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        if (trimEntries)
        {
            for (var index = 0; index < tokens.Length; index++)
            {
                tokens[index] = tokens[index].Trim();
            }
        }

        if (removeEmptyEntries)
        {
            tokens = tokens.Where(static token => token.Length > 0).ToArray();
        }

        return tokens.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(tokens);
    }

    public static ImmutableArray<string> SplitTopLevelTokens(
        string value,
        ReadOnlySpan<char> separators,
        bool trimEntries,
        bool removeEmptyEntries)
    {
        if (value is null || value.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var text = value;
        var tokens = ImmutableArray.CreateBuilder<string>();
        var tokenStart = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var angleDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';
        var escaped = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (inQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                inQuote = true;
                quoteChar = current;
                escaped = false;
                continue;
            }

            switch (current)
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
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0)
                    {
                        angleDepth--;
                    }

                    break;
            }

            if (braceDepth != 0 ||
                bracketDepth != 0 ||
                parenthesisDepth != 0 ||
                angleDepth != 0 ||
                !ContainsSeparator(separators, current))
            {
                continue;
            }

            AddToken(tokens, text, tokenStart, index, trimEntries, removeEmptyEntries);
            tokenStart = index + 1;
        }

        AddToken(tokens, text, tokenStart, text.Length, trimEntries, removeEmptyEntries);
        return tokens.Count == 0
            ? ImmutableArray<string>.Empty
            : tokens.ToImmutable();
    }

    private static bool ContainsSeparator(ReadOnlySpan<char> separators, char token)
    {
        for (var index = 0; index < separators.Length; index++)
        {
            if (separators[index] == token)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddToken(
        ImmutableArray<string>.Builder tokens,
        string source,
        int start,
        int end,
        bool trimEntries,
        bool removeEmptyEntries)
    {
        if (end < start)
        {
            return;
        }

        var token = source.Substring(start, end - start);
        if (trimEntries)
        {
            token = token.Trim();
        }

        if (removeEmptyEntries && token.Length == 0)
        {
            return;
        }

        tokens.Add(token);
    }
}
