using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlConditionalNamespaceUriSemantics
{
    public static bool TrySplit(
        string rawNamespace,
        out string normalizedNamespace,
        out string conditionExpression)
    {
        normalizedNamespace = string.Empty;
        conditionExpression = string.Empty;

        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            return false;
        }

        var separatorIndex = TopLevelTextParser.IndexOfTopLevel(rawNamespace, '?');
        if (separatorIndex <= 0 || separatorIndex >= rawNamespace.Length - 1)
        {
            return false;
        }

        var candidateCondition = rawNamespace[(separatorIndex + 1)..].Trim();
        if (candidateCondition.Length == 0)
        {
            return false;
        }

        normalizedNamespace = rawNamespace[..separatorIndex];
        conditionExpression = candidateCondition;
        return true;
    }
}
