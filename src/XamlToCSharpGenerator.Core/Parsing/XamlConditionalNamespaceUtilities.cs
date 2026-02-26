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

        if (!XamlConditionalNamespaceUriSemantics.TrySplit(
                rawNamespace,
                out var candidateNamespace,
                out var candidateCondition))
        {
            return false;
        }

        if (!XamlConditionalExpressionSemantics.TryParseMethodCallShape(
                candidateCondition,
                out _,
                out _,
                out _,
                out _))
        {
            return false;
        }

        normalizedNamespace = candidateNamespace;
        conditionExpression = candidateCondition;
        return true;
    }
}
