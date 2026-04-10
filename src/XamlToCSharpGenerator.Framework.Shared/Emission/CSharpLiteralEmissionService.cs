using System;
using System.Text;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class CSharpLiteralEmissionService
{
    public string QuoteOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "null";
        }

        return "\"" + EscapeStringLiteral(value!) + "\"";
    }

    public string BoolLiteral(bool value)
    {
        return value ? "true" : "false";
    }

    public string EscapeStringLiteral(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    public string NormalizeCommentText(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.Replace("\r", " ")
            .Replace("\n", " ");
    }
}
