using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlConditionalMethodCallSemantics
{
    private const string ApiInformationPrefix = "ApiInformation.";

    public static bool TryParseMethodCall(
        string rawExpression,
        out string normalizedExpression,
        out string methodName,
        out string argumentsText,
        out string errorMessage)
    {
        normalizedExpression = string.Empty;
        methodName = string.Empty;
        argumentsText = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            errorMessage = "Condition expression is empty.";
            return false;
        }

        var trimmedExpression = rawExpression.Trim();
        if (!TrySplitMethodCall(trimmedExpression, out var methodToken, out argumentsText))
        {
            errorMessage = "Condition expression must be a method call.";
            return false;
        }

        var normalizedMethodName = NormalizeMethodToken(methodToken);
        if (normalizedMethodName.Length == 0)
        {
            errorMessage = "Condition expression must be a method call.";
            return false;
        }

        normalizedExpression = trimmedExpression;
        methodName = normalizedMethodName;
        return true;
    }

    private static bool TrySplitMethodCall(
        string expression,
        out string methodToken,
        out string argumentsText)
    {
        methodToken = string.Empty;
        argumentsText = string.Empty;
        var openParenthesisIndex = FindTopLevelOpenParenthesis(expression);
        if (openParenthesisIndex <= 0)
        {
            return false;
        }

        methodToken = expression.Substring(0, openParenthesisIndex).Trim();
        var cursor = openParenthesisIndex;
        if (!TopLevelTextParser.TryReadBalancedContent(expression, ref cursor, '(', ')', out argumentsText))
        {
            return false;
        }

        while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor]))
        {
            cursor++;
        }

        return cursor == expression.Length;
    }

    private static int FindTopLevelOpenParenthesis(string text)
    {
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (inQuote)
            {
                if (current == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                inQuote = true;
                quoteChar = current;
                continue;
            }

            switch (current)
            {
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    continue;
                case '(':
                    if (braceDepth == 0 && bracketDepth == 0 && parenthesisDepth == 0)
                    {
                        return index;
                    }

                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    continue;
            }
        }

        return -1;
    }

    private static string NormalizeMethodToken(string methodToken)
    {
        var normalizedToken = methodToken.Trim();
        if (normalizedToken.Length >= ApiInformationPrefix.Length &&
            TopLevelTextParser.EqualsOrdinalIgnoreCaseAt(normalizedToken, 0, ApiInformationPrefix))
        {
            return normalizedToken.Substring(ApiInformationPrefix.Length);
        }

        return normalizedToken;
    }
}
