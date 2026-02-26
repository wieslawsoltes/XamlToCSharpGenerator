namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlPropertyReferenceTokenSemantics
{
    public static bool TryNormalize(string? rawToken, out string normalizedToken)
    {
        normalizedToken = string.Empty;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var token = rawToken!.Trim();
        if (token.Length >= 2 &&
            token[0] == '(' &&
            token[token.Length - 1] == ')')
        {
            token = token.Substring(1, token.Length - 2).Trim();
        }

        if (token.Length == 0)
        {
            return false;
        }

        normalizedToken = token;
        return true;
    }
}
