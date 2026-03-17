using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewTypeExpressionParser
{
    private static readonly MarkupExpressionParser MarkupParser = new();

    public static bool TryExtractTypeToken(string? typeExpression, out string token)
    {
        token = string.Empty;
        if (typeExpression is null)
        {
            return false;
        }

        var trimmed = typeExpression.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!MarkupParser.TryParseMarkupExtension(trimmed, out var markup) ||
            XamlMarkupExtensionNameSemantics.Classify(markup.Name) != XamlMarkupExtensionKind.Type)
        {
            token = trimmed;
            return true;
        }

        var typeToken = ResolveTypeMarkupPayload(markup);
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            return false;
        }

        token = XamlQuotedValueSemantics.TrimAndUnquote(typeToken.Trim());
        return token.Length > 0;
    }

    private static string? ResolveTypeMarkupPayload(MarkupExtensionInfo markup)
    {
        if (markup.NamedArguments.TryGetValue("TypeName", out var explicitTypeName))
        {
            return explicitTypeName;
        }

        if (markup.NamedArguments.TryGetValue("Type", out var explicitType))
        {
            return explicitType;
        }

        return markup.PositionalArguments.Length > 0
            ? markup.PositionalArguments[0]
            : null;
    }
}
