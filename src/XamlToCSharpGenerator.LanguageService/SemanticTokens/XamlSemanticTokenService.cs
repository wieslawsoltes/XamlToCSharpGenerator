using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.SemanticTokens;

public sealed class XamlSemanticTokenService
{
    public static readonly ImmutableArray<string> TokenTypes = ["class", "property", "string", "keyword"];

    public ImmutableArray<XamlSemanticToken> GetTokens(string text)
    {
        var tokens = ImmutableArray.CreateBuilder<XamlSemanticToken>();
        var lines = TextCoordinateHelper.SplitLines(text);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            TokenizeLine(line, lineIndex, tokens);
        }

        return tokens.ToImmutable();
    }

    private static void TokenizeLine(
        string line,
        int lineIndex,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '<')
            {
                var start = i + 1;
                if (start < line.Length && line[start] == '/')
                {
                    start++;
                }

                var length = ReadIdentifierLength(line, start);
                if (length > 0)
                {
                    tokens.Add(new XamlSemanticToken(lineIndex, start, length, "class"));
                }

                i = start + Math.Max(length, 1);
                continue;
            }

            if (line[i] == '"')
            {
                var start = i + 1;
                var end = line.IndexOf('"', start);
                if (end > start)
                {
                    tokens.Add(new XamlSemanticToken(lineIndex, start, end - start, "string"));
                    i = end + 1;
                    continue;
                }
            }

            if (char.IsLetter(line[i]) || line[i] == 'x')
            {
                var identifierLength = ReadIdentifierLength(line, i);
                var afterIdentifier = i + identifierLength;
                if (afterIdentifier < line.Length && line[afterIdentifier] == '=')
                {
                    tokens.Add(new XamlSemanticToken(lineIndex, i, identifierLength, "property"));
                    i = afterIdentifier + 1;
                    continue;
                }
            }

            i++;
        }
    }

    private static int ReadIdentifierLength(string line, int start)
    {
        if (start < 0 || start >= line.Length)
        {
            return 0;
        }

        var length = 0;
        var index = start;
        while (index < line.Length)
        {
            var ch = line[index];
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.'))
            {
                break;
            }

            length++;
            index++;
        }

        return length;
    }
}
