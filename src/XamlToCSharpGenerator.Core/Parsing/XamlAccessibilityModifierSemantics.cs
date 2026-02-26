using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlAccessibilityModifierSemantics
{
    public static string? NormalizeFieldModifier(string? modifier)
    {
        if (!TryNormalize(modifier, out var normalized))
        {
            return null;
        }

        return normalized switch
        {
            "private" => "private",
            "public" => "public",
            "protected" => "protected",
            "internal" => "internal",
            "notpublic" => "internal",
            _ => null
        };
    }

    public static string? NormalizeClassModifier(string? modifier)
    {
        if (!TryNormalize(modifier, out var normalized))
        {
            return null;
        }

        return normalized switch
        {
            "public" => "public",
            "internal" => "internal",
            "private" => "private",
            "protected" => "protected",
            "protected internal" => "protected internal",
            "private protected" => "private protected",
            "notpublic" => "internal",
            _ => null
        };
    }

    private static bool TryNormalize(string? modifier, out string normalized)
    {
        normalized = string.Empty;
        if (modifier is null)
        {
            return false;
        }

        var token = modifier.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        normalized = CollapseWhitespace(token).ToLowerInvariant();
        return normalized.Length > 0;
    }

    private static string CollapseWhitespace(string token)
    {
        Span<char> buffer = stackalloc char[token.Length];
        var writeIndex = 0;
        var previousWhitespace = false;

        for (var index = 0; index < token.Length; index++)
        {
            var current = token[index];
            if (char.IsWhiteSpace(current))
            {
                if (previousWhitespace)
                {
                    continue;
                }

                buffer[writeIndex++] = ' ';
                previousWhitespace = true;
            }
            else
            {
                buffer[writeIndex++] = current;
                previousWhitespace = false;
            }
        }

        if (writeIndex == 0)
        {
            return string.Empty;
        }

        if (buffer[0] == ' ')
        {
            buffer = buffer.Slice(1);
            writeIndex--;
        }

        if (writeIndex > 0 && buffer[writeIndex - 1] == ' ')
        {
            writeIndex--;
        }

        var result = new char[writeIndex];
        buffer.Slice(0, writeIndex).CopyTo(result);
        return new string(result);
    }
}
