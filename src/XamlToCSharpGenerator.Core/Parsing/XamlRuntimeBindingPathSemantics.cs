using System;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

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

        var index = 0;
        if (!CompiledBindingPathSegmentSemantics.TryParseCastTypeToken(
                normalized,
                ref index,
                out var parsedTypeToken,
                out _,
                out _))
        {
            return false;
        }

        if (index >= normalized.Length ||
            normalized[index] != '.')
        {
            return false;
        }

        typeToken = parsedTypeToken;
        remainder = normalized[(index + 1)..].Trim();
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
