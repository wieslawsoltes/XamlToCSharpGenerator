using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlTypeTokenSemantics
{
    private const string GlobalQualifier = "global::";
    private const string XamlDirectivePrefix = "x:";

    public static string TrimGlobalQualifier(string token)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        var normalized = token.Trim();
        if (normalized.StartsWith(GlobalQualifier, StringComparison.Ordinal))
        {
            return normalized.Substring(GlobalQualifier.Length);
        }

        return normalized;
    }

    public static string TrimXamlDirectivePrefix(string token)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        var normalized = token.Trim();
        if (normalized.StartsWith(XamlDirectivePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(XamlDirectivePrefix.Length);
        }

        return normalized;
    }
}
