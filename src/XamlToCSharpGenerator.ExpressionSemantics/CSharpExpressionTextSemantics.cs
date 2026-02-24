using System;
using System.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public static class CSharpExpressionTextSemantics
{
    public static string NormalizeExpressionCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var normalized = ReplaceExpressionOperatorAliases(code.Trim());
        normalized = NormalizeSingleQuotedExpressionStrings(normalized);
        return normalized.Trim();
    }

    public static bool ContainsImplicitExpressionOperator(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            if (!inDoubleQuotedString &&
                ch == '\'' &&
                !IsEscapedChar(expression, index))
            {
                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (!inSingleQuotedString &&
                ch == '"' &&
                !IsEscapedChar(expression, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (inSingleQuotedString || inDoubleQuotedString)
            {
                continue;
            }

            if (index + 1 < expression.Length)
            {
                var twoChars = expression.Substring(index, 2);
                if (twoChars is "=>" or "??" or "?." or "&&" or "||" or "==" or "!=" or "<=" or ">=" or "<<" or ">>" or "++" or "--")
                {
                    return true;
                }
            }

            if (ch is '+' or '-' or '*' or '/' or '%' or '<' or '>' or '?' or ':')
            {
                return true;
            }
        }

        foreach (var alias in ExpressionOperatorAliases)
        {
            if (ContainsAliasToken(expression, alias))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsMethodCallLikeExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();
        if (!IsIdentifierStart(trimmed[0]))
        {
            return false;
        }

        var index = 1;
        while (index < trimmed.Length && IsIdentifierPart(trimmed[index]))
        {
            index++;
        }

        while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
        {
            index++;
        }

        return index < trimmed.Length && trimmed[index] == '(';
    }

    public static bool IsMemberAccessLikeExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();
        var separator = trimmed.IndexOf('.');
        if (separator <= 0 || separator >= trimmed.Length - 1)
        {
            return false;
        }

        var first = trimmed.Substring(0, separator).Trim();
        var second = trimmed.Substring(separator + 1).Trim();
        return IsBareIdentifierExpression(first) && IsBareIdentifierExpression(second);
    }

    public static bool IsBareIdentifierExpression(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        if (!IsIdentifierStart(trimmed[0]))
        {
            return false;
        }

        for (var index = 1; index < trimmed.Length; index++)
        {
            if (!IsIdentifierPart(trimmed[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] ExpressionOperatorAliases =
    [
        "AND",
        "OR",
        "LT",
        "GT",
        "LTE",
        "GTE"
    ];

    private static bool ContainsAliasToken(string expression, string alias)
    {
        var searchStart = 0;
        while (searchStart < expression.Length)
        {
            var index = expression.IndexOf(alias, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeBoundary = index == 0 || !IsIdentifierPart(expression[index - 1]);
            var afterIndex = index + alias.Length;
            var afterBoundary = afterIndex >= expression.Length || !IsIdentifierPart(expression[afterIndex]);
            if (beforeBoundary && afterBoundary)
            {
                return true;
            }

            searchStart = index + alias.Length;
        }

        return false;
    }

    private static string ReplaceExpressionOperatorAliases(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var result = new StringBuilder(code.Length);
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var index = 0;
        while (index < code.Length)
        {
            var ch = code[index];
            if (!inDoubleQuotedString &&
                ch == '\'' &&
                !IsEscapedChar(code, index))
            {
                inSingleQuotedString = !inSingleQuotedString;
                result.Append(ch);
                index++;
                continue;
            }

            if (!inSingleQuotedString &&
                ch == '"' &&
                !IsEscapedChar(code, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                result.Append(ch);
                index++;
                continue;
            }

            if (inSingleQuotedString || inDoubleQuotedString || !IsIdentifierStart(ch))
            {
                result.Append(ch);
                index++;
                continue;
            }

            var start = index;
            index++;
            while (index < code.Length && IsIdentifierPart(code[index]))
            {
                index++;
            }

            var token = code.Substring(start, index - start);
            if (TryMapExpressionAliasToken(token, out var replacement) &&
                (start == 0 || !IsIdentifierPart(code[start - 1])) &&
                (index >= code.Length || !IsIdentifierPart(code[index])))
            {
                result.Append(replacement);
            }
            else
            {
                result.Append(token);
            }
        }

        return result.ToString();
    }

    private static bool TryMapExpressionAliasToken(string token, out string replacement)
    {
        replacement = token;
        if (token.Equals("AND", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "&&";
            return true;
        }

        if (token.Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "||";
            return true;
        }

        if (token.Equals("LT", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "<";
            return true;
        }

        if (token.Equals("GT", StringComparison.OrdinalIgnoreCase))
        {
            replacement = ">";
            return true;
        }

        if (token.Equals("LTE", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "<=";
            return true;
        }

        if (token.Equals("GTE", StringComparison.OrdinalIgnoreCase))
        {
            replacement = ">=";
            return true;
        }

        return false;
    }

    private static string NormalizeSingleQuotedExpressionStrings(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var result = new StringBuilder(code.Length);
        var inDoubleQuotedString = false;
        var index = 0;
        while (index < code.Length)
        {
            var ch = code[index];
            if (ch == '"' && !IsEscapedChar(code, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                result.Append(ch);
                index++;
                continue;
            }

            if (inDoubleQuotedString || ch != '\'' || IsEscapedChar(code, index))
            {
                result.Append(ch);
                index++;
                continue;
            }

            var literalStart = index + 1;
            var cursor = literalStart;
            while (cursor < code.Length)
            {
                if (code[cursor] == '\'' && !IsEscapedChar(code, cursor))
                {
                    break;
                }

                cursor++;
            }

            if (cursor >= code.Length)
            {
                result.Append(ch);
                index++;
                continue;
            }

            var literalContent = code.Substring(literalStart, cursor - literalStart);
            if (LooksLikeCharLiteralContent(literalContent))
            {
                result.Append(code, index, cursor - index + 1);
                index = cursor + 1;
                continue;
            }

            result.Append('"');
            for (var contentIndex = 0; contentIndex < literalContent.Length; contentIndex++)
            {
                var contentChar = literalContent[contentIndex];
                if (contentChar == '\\' &&
                    contentIndex + 1 < literalContent.Length &&
                    literalContent[contentIndex + 1] == '\'')
                {
                    result.Append('\'');
                    contentIndex++;
                    continue;
                }

                if (contentChar == '"')
                {
                    result.Append("\\\"");
                    continue;
                }

                result.Append(contentChar);
            }

            result.Append('"');
            index = cursor + 1;
        }

        return result.ToString();
    }

    private static bool LooksLikeCharLiteralContent(string content)
    {
        if (content.Length == 1)
        {
            return true;
        }

        if (content.StartsWith("\\", StringComparison.Ordinal))
        {
            if (content.Length == 2)
            {
                return true;
            }

            if (content.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) && content.Length == 6)
            {
                return true;
            }

            if (content.StartsWith("\\x", StringComparison.OrdinalIgnoreCase) &&
                content.Length >= 3 &&
                content.Length <= 5)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEscapedChar(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return false;
        }

        var escapeCount = 0;
        for (var current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            escapeCount++;
        }

        return escapeCount % 2 == 1;
    }

    private static bool IsIdentifierStart(char ch)
    {
        return MiniLanguageSyntaxFacts.IsIdentifierStart(ch);
    }

    private static bool IsIdentifierPart(char ch)
    {
        return MiniLanguageSyntaxFacts.IsIdentifierPart(ch);
    }
}
