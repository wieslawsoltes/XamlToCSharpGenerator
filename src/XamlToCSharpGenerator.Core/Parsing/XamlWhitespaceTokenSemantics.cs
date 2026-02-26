using System;
using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlWhitespaceTokenSemantics
{
    public static bool ContainsWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var text = value!;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    public static ImmutableArray<string> SplitTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ImmutableArray<string>.Empty;
        }

        var text = value!;
        var tokens = ImmutableArray.CreateBuilder<string>();
        var tokenStart = -1;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current))
            {
                FlushToken(tokens, text, tokenStart, index);
                tokenStart = -1;
                continue;
            }

            if (tokenStart < 0)
            {
                tokenStart = index;
            }
        }

        FlushToken(tokens, text, tokenStart, text.Length);
        return tokens.Count == 0 ? ImmutableArray<string>.Empty : tokens.ToImmutable();
    }

    private static void FlushToken(
        ImmutableArray<string>.Builder tokens,
        string source,
        int tokenStart,
        int tokenEnd)
    {
        if (tokenStart < 0 || tokenEnd <= tokenStart)
        {
            return;
        }

        tokens.Add(source.Substring(tokenStart, tokenEnd - tokenStart));
    }
}
