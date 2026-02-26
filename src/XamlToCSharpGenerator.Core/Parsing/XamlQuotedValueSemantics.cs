using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlQuotedValueSemantics
{
    public static string TrimAndUnquote(string value)
    {
        return UnquoteWrapped(value.Trim());
    }

    public static bool IsWrapped(string value)
    {
        return value.Length >= 2 &&
               ((value[0] == '"' && value[value.Length - 1] == '"') ||
                (value[0] == '\'' && value[value.Length - 1] == '\''));
    }

    public static string UnquoteWrapped(string value)
    {
        if (IsWrapped(value))
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }
}
