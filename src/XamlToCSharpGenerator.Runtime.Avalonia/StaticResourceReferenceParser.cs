using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

internal static class StaticResourceReferenceParser
{
    public static bool TryExtractResourceKey(string? expression, out string resourceKey)
    {
        resourceKey = string.Empty;
        if (expression is null)
        {
            return false;
        }

        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!LooksLikeMarkupExtension(trimmed))
        {
            resourceKey = Unquote(trimmed).Trim();
            return resourceKey.Length > 0;
        }

        if (!TryParseMarkupHeadAndArguments(trimmed, out var markupName, out var argumentsText))
        {
            return false;
        }

        if (!IsStaticOrDynamicResourceMarkup(markupName))
        {
            return false;
        }

        if (!TrySplitArguments(argumentsText, out var arguments))
        {
            return false;
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];
            var equalsIndex = IndexOfTopLevel(token, '=');
            if (equalsIndex <= 0 || equalsIndex >= token.Length - 1)
            {
                continue;
            }

            var argumentName = token.Substring(0, equalsIndex).Trim();
            if (!argumentName.Equals("ResourceKey", StringComparison.OrdinalIgnoreCase) &&
                !argumentName.Equals("Key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resourceKey = Unquote(token.Substring(equalsIndex + 1)).Trim();
            return resourceKey.Length > 0;
        }

        resourceKey = Unquote(arguments[0]).Trim();
        return resourceKey.Length > 0;
    }

    private static bool LooksLikeMarkupExtension(string value)
    {
        return value.Length >= 2 &&
               value[0] == '{' &&
               value[value.Length - 1] == '}';
    }

    private static bool TryParseMarkupHeadAndArguments(string value, out string markupName, out string argumentsText)
    {
        markupName = string.Empty;
        argumentsText = string.Empty;

        var inner = value.Substring(1, value.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return false;
        }

        var headLength = 0;
        while (headLength < inner.Length &&
               !char.IsWhiteSpace(inner[headLength]) &&
               inner[headLength] != ',')
        {
            headLength++;
        }

        if (headLength == 0)
        {
            return false;
        }

        markupName = inner.Substring(0, headLength).Trim();
        argumentsText = headLength < inner.Length ? inner.Substring(headLength).Trim() : string.Empty;
        if (argumentsText.StartsWith(",", StringComparison.Ordinal))
        {
            argumentsText = argumentsText.Substring(1).TrimStart();
        }

        return markupName.Length > 0;
    }

    private static bool TrySplitArguments(string argumentsText, out IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            arguments = Array.Empty<string>();
            return false;
        }

        var values = new List<string>();
        foreach (var token in SplitTopLevel(argumentsText, ','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            values.Add(trimmed);
        }

        arguments = values;
        return values.Count > 0;
    }

    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        var start = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
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
                yield return value.Substring(start, index - start);
                start = index + 1;
            }
        }

        yield return value.Substring(start);
    }

    private static int IndexOfTopLevel(string value, char token)
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

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
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

    private static bool IsStaticOrDynamicResourceMarkup(string markupName)
    {
        return markupName.Equals("StaticResource", StringComparison.OrdinalIgnoreCase) ||
               markupName.Equals("DynamicResource", StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }
}
