using System;
using System.Collections.Generic;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public static class StaticResourceReferenceParser
{
    public static bool TryExtractResourceKey(string? expression, out string resourceKey)
    {
        resourceKey = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();
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
            var equalsIndex = TopLevelTextParser.IndexOfTopLevel(token, '=');
            if (equalsIndex <= 0 || equalsIndex >= token.Length - 1)
            {
                continue;
            }

            var argumentName = token[..equalsIndex].Trim();
            if (!argumentName.Equals("ResourceKey", StringComparison.OrdinalIgnoreCase) &&
                !argumentName.Equals("Key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resourceKey = Unquote(token[(equalsIndex + 1)..]).Trim();
            return resourceKey.Length > 0;
        }

        resourceKey = Unquote(arguments[0]).Trim();
        return resourceKey.Length > 0;
    }

    private static bool LooksLikeMarkupExtension(string value)
    {
        return value.Length >= 2 &&
               value[0] == '{' &&
               value[^1] == '}';
    }

    private static bool TryParseMarkupHeadAndArguments(
        string value,
        out string markupName,
        out string argumentsText)
    {
        markupName = string.Empty;
        argumentsText = string.Empty;

        var inner = value[1..^1].Trim();
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

        markupName = inner[..headLength].Trim();
        argumentsText = headLength < inner.Length ? inner[headLength..].Trim() : string.Empty;
        if (argumentsText.StartsWith(",", StringComparison.Ordinal))
        {
            argumentsText = argumentsText[1..].TrimStart();
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
        foreach (var token in TopLevelTextParser.SplitTopLevel(argumentsText, ','))
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

    private static bool IsStaticOrDynamicResourceMarkup(string markupName)
    {
        return markupName.Equals("StaticResource", StringComparison.OrdinalIgnoreCase) ||
               markupName.Equals("DynamicResource", StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
