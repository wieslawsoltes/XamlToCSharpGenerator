namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlReferenceNameSemantics
{
    public static bool TryNormalizeReferenceName(string? rawName, out string normalizedName)
    {
        normalizedName = string.Empty;
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        var unquoted = XamlQuotedValueSemantics.TrimAndUnquote(rawName!);
        if (unquoted.Length == 0 || XamlWhitespaceTokenSemantics.ContainsWhitespace(unquoted))
        {
            return false;
        }

        normalizedName = unquoted;
        return true;
    }
}
