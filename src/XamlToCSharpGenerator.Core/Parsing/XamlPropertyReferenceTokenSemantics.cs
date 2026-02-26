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
            token[^1] == ')')
        {
            token = token[1..^1].Trim();
        }

        if (token.Length == 0)
        {
            return false;
        }

        normalizedToken = token;
        return true;
    }
}
