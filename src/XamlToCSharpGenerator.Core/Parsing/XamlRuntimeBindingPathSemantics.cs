using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlRuntimeBindingPathSemantics
{
    public static string NormalizePath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var normalized = path.Trim();
        if (!TrySplitTypeCastPrefix(normalized, out var typeToken, out var remainder))
        {
            return normalized;
        }

        if (!IsTypeCastToken(typeToken))
        {
            return normalized;
        }

        return remainder.Length == 0 ? "." : remainder;
    }

    public static bool TrySplitTypeCastPrefix(string path, out string typeToken, out string remainder)
    {
        typeToken = string.Empty;
        remainder = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Trim();
        if (normalized.Length < 4 || normalized[0] != '(')
        {
            return false;
        }

        var closingParenthesisIndex = normalized.IndexOf(')');
        if (closingParenthesisIndex <= 1 ||
            closingParenthesisIndex + 1 >= normalized.Length ||
            normalized[closingParenthesisIndex + 1] != '.')
        {
            return false;
        }

        typeToken = normalized.Substring(1, closingParenthesisIndex - 1).Trim();
        remainder = normalized.Substring(closingParenthesisIndex + 2).Trim();
        return true;
    }

    public static bool IsTypeCastToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim();
        if (normalized.IndexOfAny([' ', '[', ']']) >= 0)
        {
            return false;
        }

        return normalized.IndexOf(':') >= 0 ||
               normalized.StartsWith("global::", StringComparison.Ordinal);
    }
}
