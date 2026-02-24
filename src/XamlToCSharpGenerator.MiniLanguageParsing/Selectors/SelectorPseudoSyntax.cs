using System.Globalization;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public enum SelectorPseudoFunctionKind
{
    Unknown = 0,
    Is = 1,
    Not = 2,
    NthChild = 3,
    NthLastChild = 4,
}

public static class SelectorPseudoSyntax
{
    public static SelectorPseudoFunctionKind ClassifyPseudoFunction(string pseudoName)
    {
        if (string.IsNullOrWhiteSpace(pseudoName))
        {
            return SelectorPseudoFunctionKind.Unknown;
        }

        return pseudoName.Trim().ToLowerInvariant() switch
        {
            "is" => SelectorPseudoFunctionKind.Is,
            "not" => SelectorPseudoFunctionKind.Not,
            "nth-child" => SelectorPseudoFunctionKind.NthChild,
            "nth-last-child" => SelectorPseudoFunctionKind.NthLastChild,
            _ => SelectorPseudoFunctionKind.Unknown
        };
    }

    public static bool TryParseNthChildExpression(string pseudoArgument, out int step, out int offset)
    {
        step = 0;
        offset = 0;

        var text = pseudoArgument.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return false;
        }

        if (text == "odd")
        {
            step = 2;
            offset = 1;
            return true;
        }

        if (text == "even")
        {
            step = 2;
            offset = 0;
            return true;
        }

        var compact = text.Replace(" ", string.Empty);
        var nIndex = compact.IndexOf('n');
        if (nIndex < 0)
        {
            if (!int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
            {
                return false;
            }

            step = 0;
            return true;
        }

        var stepToken = compact.Substring(0, nIndex);
        if (stepToken.Length == 0 || stepToken == "+")
        {
            step = 1;
        }
        else if (stepToken == "-")
        {
            step = -1;
        }
        else if (!int.TryParse(stepToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out step))
        {
            return false;
        }

        var offsetToken = compact.Substring(nIndex + 1);
        if (offsetToken.Length == 0)
        {
            offset = 0;
            return true;
        }

        return int.TryParse(offsetToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
    }
}
