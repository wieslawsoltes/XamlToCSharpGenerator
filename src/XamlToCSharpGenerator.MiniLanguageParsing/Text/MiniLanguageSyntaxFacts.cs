using System.Globalization;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Text;

public static class MiniLanguageSyntaxFacts
{
    public static bool IsIdentifierStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    public static bool IsIdentifierPart(char ch)
    {
        if (IsIdentifierStart(ch))
        {
            return true;
        }

        var category = char.GetUnicodeCategory(ch);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.Format
            or UnicodeCategory.DecimalDigitNumber;
    }

    public static bool IsStyleClassPart(char ch)
    {
        if (IsIdentifierPart(ch) || ch == '-')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(ch);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.Format
            or UnicodeCategory.DecimalDigitNumber;
    }
}
