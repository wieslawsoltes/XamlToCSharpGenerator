using System;
using System.Collections.Immutable;
using System.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlListValueSemantics
{
    public static ImmutableArray<string> SplitWhitespaceAndCommaTokens(string value)
    {
        return SplitTokens(value, splitOnWhitespace: true, splitOnComma: true, preserveQuotedSections: false);
    }

    public static ImmutableArray<string> SplitCommaSeparatedTokens(string value)
    {
        return SplitTokens(value, splitOnWhitespace: false, splitOnComma: true, preserveQuotedSections: true);
    }

    private static ImmutableArray<string> SplitTokens(
        string value,
        bool splitOnWhitespace,
        bool splitOnComma,
        bool preserveQuotedSections)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var tokenBuilder = new StringBuilder(value.Length);
        var quoteChar = '\0';
        var escaped = false;

        void FlushToken()
        {
            if (tokenBuilder.Length == 0)
            {
                return;
            }

            var token = tokenBuilder.ToString().Trim();
            tokenBuilder.Clear();
            if (token.Length == 0)
            {
                return;
            }

            builder.Add(Unquote(token));
        }

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (preserveQuotedSections)
            {
                if (quoteChar != '\0')
                {
                    tokenBuilder.Append(current);
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
                        quoteChar = '\0';
                    }

                    continue;
                }

                if (current is '"' or '\'')
                {
                    quoteChar = current;
                    tokenBuilder.Append(current);
                    continue;
                }
            }

            if (splitOnComma && current == ',')
            {
                FlushToken();
                continue;
            }

            if (splitOnWhitespace && char.IsWhiteSpace(current))
            {
                FlushToken();
                continue;
            }

            tokenBuilder.Append(current);
        }

        FlushToken();
        return builder.Count == 0
            ? ImmutableArray<string>.Empty
            : builder.ToImmutable();
    }

    private static string Unquote(string token)
    {
        if (token.Length >= 2 &&
            ((token[0] == '"' && token[^1] == '"') ||
             (token[0] == '\'' && token[^1] == '\'')))
        {
            return token.Substring(1, token.Length - 2);
        }

        return token;
    }
}
