using System;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public static class MarkupExpressionEnvelopeSemantics
{
    public static bool IsMarkupExpression(string value)
    {
        return TryExtractInnerContent(value, out _);
    }

    public static bool TryExtractInnerContent(string value, out string innerContent)
    {
        innerContent = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        innerContent = trimmed.Substring(1, trimmed.Length - 2).Trim();
        return innerContent.Length > 0;
    }
}
