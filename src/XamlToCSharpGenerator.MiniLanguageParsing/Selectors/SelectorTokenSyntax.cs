using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public static class SelectorTokenSyntax
{
    public static bool TryParseIdentifierToken(string text, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= text.Length || !MiniLanguageSyntaxFacts.IsIdentifierStart(text[index]))
        {
            return false;
        }

        var start = index;
        index++;
        while (index < text.Length && MiniLanguageSyntaxFacts.IsIdentifierPart(text[index]))
        {
            index++;
        }

        token = text.Substring(start, index - start);
        return true;
    }

    public static bool TryParseStyleClassToken(string text, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= text.Length || !MiniLanguageSyntaxFacts.IsIdentifierStart(text[index]))
        {
            return false;
        }

        var start = index;
        index++;
        while (index < text.Length && MiniLanguageSyntaxFacts.IsStyleClassPart(text[index]))
        {
            index++;
        }

        token = text.Substring(start, index - start);
        return true;
    }

    public static bool IsValidIdentifier(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !MiniLanguageSyntaxFacts.IsIdentifierStart(text[0]))
        {
            return false;
        }

        for (var index = 1; index < text.Length; index++)
        {
            if (!MiniLanguageSyntaxFacts.IsIdentifierPart(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryReadStandaloneTypeToken(string text, out string typeToken)
    {
        typeToken = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var index = 0;
        if (!SelectorBranchTokenizer.TryReadTypeToken(text, ref index, out var parsedTypeToken) ||
            string.IsNullOrWhiteSpace(parsedTypeToken))
        {
            return false;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index != text.Length)
        {
            return false;
        }

        typeToken = parsedTypeToken!;
        return true;
    }
}
