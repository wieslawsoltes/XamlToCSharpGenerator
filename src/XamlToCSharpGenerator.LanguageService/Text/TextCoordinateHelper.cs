using System;
using System.Collections.Generic;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Text;

internal static class TextCoordinateHelper
{
    public static int GetOffset(string text, SourcePosition position)
    {
        if (position.Line < 0 || position.Character < 0)
        {
            return 0;
        }

        var targetLine = position.Line;
        var targetCharacter = position.Character;
        var line = 0;
        var index = 0;

        while (index < text.Length && line < targetLine)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                line++;
            }
            else if (text[index] == '\n')
            {
                line++;
            }

            index++;
        }

        if (line < targetLine)
        {
            return text.Length;
        }

        var lineStart = index;
        var lineLength = 0;
        while (index < text.Length && text[index] != '\r' && text[index] != '\n')
        {
            lineLength++;
            index++;
        }

        var clampedCharacter = Math.Min(targetCharacter, lineLength);
        return lineStart + clampedCharacter;
    }

    public static SourcePosition GetPosition(string text, int offset)
    {
        if (offset <= 0)
        {
            return new SourcePosition(0, 0);
        }

        var boundedOffset = Math.Min(offset, text.Length);
        var line = 0;
        var character = 0;

        for (var i = 0; i < boundedOffset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                character = 0;
                continue;
            }

            character++;
        }

        return new SourcePosition(line, character);
    }

    public static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }
}
