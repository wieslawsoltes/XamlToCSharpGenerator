using System;

namespace XamlToCSharpGenerator.Runtime;

internal static class MarkupExpressionEnvelopeSemantics
{
    public static bool IsMarkupExpression(string value)
    {
        return TryExtractInnerContent(value, out _);
    }

    public static string UnescapeEscapedLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var firstNonWhitespaceIndex = 0;
        while (firstNonWhitespaceIndex < value.Length &&
               char.IsWhiteSpace(value[firstNonWhitespaceIndex]))
        {
            firstNonWhitespaceIndex++;
        }

        if (firstNonWhitespaceIndex + 1 >= value.Length ||
            value[firstNonWhitespaceIndex] != '{' ||
            value[firstNonWhitespaceIndex + 1] != '}')
        {
            return value;
        }

        return value.Remove(firstNonWhitespaceIndex, 2);
    }

    public static bool TryExtractInnerContent(string value, out string innerContent)
    {
        innerContent = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            return false;
        }

        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        innerContent = trimmed.Substring(1, trimmed.Length - 2).Trim();
        return innerContent.Length > 0;
    }
}
