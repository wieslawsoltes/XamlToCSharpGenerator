using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlAvaloniaKeyGestureLiteralSemantics
{
    public static bool TryParse(
        string? value,
        out string? keyToken,
        out string[] modifierTokens)
    {
        keyToken = null;
        modifierTokens = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!TrySplitTokens(value!, out var tokens) || tokens.Length == 0)
        {
            return false;
        }

        var normalizedModifiers = new List<string>(tokens.Length);
        var seenModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            var token = tokens[index];
            if (!TryNormalizeModifierToken(token, out var normalizedModifierToken))
            {
                return false;
            }

            if (seenModifiers.Add(normalizedModifierToken))
            {
                normalizedModifiers.Add(normalizedModifierToken);
            }
        }

        var lastToken = tokens[tokens.Length - 1];
        if (TryNormalizeModifierToken(lastToken, out var trailingModifierToken))
        {
            if (seenModifiers.Add(trailingModifierToken))
            {
                normalizedModifiers.Add(trailingModifierToken);
            }
        }
        else if (TryNormalizeKeyToken(lastToken, out var normalizedKeyToken))
        {
            keyToken = normalizedKeyToken;
        }
        else
        {
            return false;
        }

        modifierTokens = normalizedModifiers.Count == 0
            ? Array.Empty<string>()
            : normalizedModifiers.ToArray();
        return true;
    }

    private static bool TrySplitTokens(string value, out string[] tokens)
    {
        var parts = new List<string>();
        var start = 0;
        for (var index = 0; index <= value.Length; index++)
        {
            var isLast = index == value.Length;
            var character = isLast ? '\0' : value[index];
            if (!isLast && (character != '+' || start == index))
            {
                continue;
            }

            var token = value.Substring(start, index - start).Trim();
            if (token.Length == 0)
            {
                tokens = Array.Empty<string>();
                return false;
            }

            parts.Add(token);
            start = index + 1;
        }

        tokens = parts.ToArray();
        return true;
    }

    private static bool TryNormalizeKeyToken(string token, out string normalizedToken)
    {
        normalizedToken = string.Empty;
        if (token.Equals("+", StringComparison.Ordinal))
        {
            normalizedToken = "OemPlus";
            return true;
        }

        if (token.Equals("-", StringComparison.Ordinal))
        {
            normalizedToken = "OemMinus";
            return true;
        }

        if (token.Equals(".", StringComparison.Ordinal))
        {
            normalizedToken = "OemPeriod";
            return true;
        }

        if (token.Equals(",", StringComparison.Ordinal))
        {
            normalizedToken = "OemComma";
            return true;
        }

        return XamlIdentifierSemantics.TryNormalizeIdentifier(token, out normalizedToken);
    }

    private static bool TryNormalizeModifierToken(string token, out string normalizedToken)
    {
        normalizedToken = string.Empty;
        if (token.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("control", StringComparison.OrdinalIgnoreCase))
        {
            normalizedToken = "Control";
            return true;
        }

        if (token.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("win", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("⌘", StringComparison.OrdinalIgnoreCase))
        {
            normalizedToken = "Meta";
            return true;
        }

        if (token.Equals("alt", StringComparison.OrdinalIgnoreCase))
        {
            normalizedToken = "Alt";
            return true;
        }

        if (token.Equals("shift", StringComparison.OrdinalIgnoreCase))
        {
            normalizedToken = "Shift";
            return true;
        }

        if (token.Equals("meta", StringComparison.OrdinalIgnoreCase))
        {
            normalizedToken = "Meta";
            return true;
        }

        return false;
    }
}
