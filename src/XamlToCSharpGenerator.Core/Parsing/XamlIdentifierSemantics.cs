using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlIdentifierSemantics
{
    public static bool IsIdentifier(string? value)
    {
        return TryNormalizeIdentifier(value, out _);
    }

    public static bool TryNormalizeIdentifier(string? value, out string normalizedIdentifier)
    {
        normalizedIdentifier = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value!.Trim();
        if (!MiniLanguageSyntaxFacts.IsIdentifierStart(candidate[0]))
        {
            return false;
        }

        for (var index = 1; index < candidate.Length; index++)
        {
            if (!MiniLanguageSyntaxFacts.IsIdentifierPart(candidate[index]))
            {
                return false;
            }
        }

        normalizedIdentifier = candidate;
        return true;
    }
}
