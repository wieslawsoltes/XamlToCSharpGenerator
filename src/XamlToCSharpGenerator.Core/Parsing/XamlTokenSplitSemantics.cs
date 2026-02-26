using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlTokenSplitSemantics
{
    public static bool TrySplitAtFirstSeparator(
        string token,
        char separator,
        out string leftToken,
        out string rightToken)
    {
        leftToken = string.Empty;
        rightToken = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        var separatorIndex = trimmed.IndexOf(separator);
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
        {
            return false;
        }

        leftToken = trimmed.Substring(0, separatorIndex).Trim();
        rightToken = trimmed.Substring(separatorIndex + 1).Trim();
        return leftToken.Length > 0 && rightToken.Length > 0;
    }

    public static bool TrySplitAtLastSeparator(
        string token,
        char separator,
        out string leftToken,
        out string rightToken)
    {
        leftToken = string.Empty;
        rightToken = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        var separatorIndex = trimmed.LastIndexOf(separator);
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
        {
            return false;
        }

        leftToken = trimmed.Substring(0, separatorIndex).Trim();
        rightToken = trimmed.Substring(separatorIndex + 1).Trim();
        return leftToken.Length > 0 && rightToken.Length > 0;
    }

    public static string TrimTerminalSuffix(string token, string suffix)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(suffix))
        {
            return token ?? string.Empty;
        }

        var trimmed = token.Trim();
        if (trimmed.Length > suffix.Length &&
            trimmed.EndsWith(suffix, StringComparison.Ordinal))
        {
            return trimmed.Substring(0, trimmed.Length - suffix.Length);
        }

        return trimmed;
    }
}
