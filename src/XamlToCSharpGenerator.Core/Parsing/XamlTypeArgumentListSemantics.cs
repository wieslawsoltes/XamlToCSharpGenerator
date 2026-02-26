using System;
using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlTypeArgumentListSemantics
{
    public static ImmutableArray<string> Parse(string? rawTypeArguments)
    {
        if (string.IsNullOrWhiteSpace(rawTypeArguments))
        {
            return ImmutableArray<string>.Empty;
        }

        var text = rawTypeArguments!;
        var builder = ImmutableArray.CreateBuilder<string>();
        var tokenStart = 0;
        var braceDepth = 0;
        var parenthesisDepth = 0;
        var angleDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < text.Length; index++)
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
                case ',' when braceDepth == 0 && parenthesisDepth == 0 && angleDepth == 0:
                    AddToken(builder, text, tokenStart, index);
                    tokenStart = index + 1;
                    break;
            }
        }

        AddToken(builder, text, tokenStart, text.Length);
        return builder.ToImmutable();
    }

    private static void AddToken(
        ImmutableArray<string>.Builder builder,
        string source,
        int start,
        int end)
    {
        if (end < start)
        {
            return;
        }

        var token = source.Substring(start, end - start).Trim();
        if (token.Length > 0)
        {
            builder.Add(token);
        }
    }
}
