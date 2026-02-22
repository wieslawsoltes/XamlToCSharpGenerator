using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlConditionalNamespaceUtilities
{
    public static string NormalizeXmlNamespace(string rawNamespace)
    {
        if (TrySplitConditionalNamespaceUri(rawNamespace, out var normalizedNamespace, out _))
        {
            return normalizedNamespace;
        }

        return rawNamespace;
    }

    public static ConditionalXamlExpression? TryGetConditionalExpression(
        string rawNamespace,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        return conditionalNamespacesByRawUri.TryGetValue(rawNamespace, out var condition)
            ? condition
            : null;
    }

    public static bool TrySplitConditionalNamespaceUri(
        string rawNamespace,
        out string normalizedNamespace,
        out string? conditionExpression)
    {
        normalizedNamespace = rawNamespace;
        conditionExpression = null;

        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            return false;
        }

        var separatorIndex = rawNamespace.IndexOf('?');
        if (separatorIndex <= 0 || separatorIndex >= rawNamespace.Length - 1)
        {
            return false;
        }

        var candidateCondition = rawNamespace.Substring(separatorIndex + 1).Trim();
        if (candidateCondition.Length == 0 ||
            !candidateCondition.EndsWith(")", System.StringComparison.Ordinal) ||
            candidateCondition.IndexOf('(') <= 0)
        {
            return false;
        }

        normalizedNamespace = rawNamespace.Substring(0, separatorIndex);
        conditionExpression = candidateCondition;
        return true;
    }
}
