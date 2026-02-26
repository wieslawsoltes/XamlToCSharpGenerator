using System;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlEventHandlerNameSemantics
{
    public static bool TryParseHandlerName(string value, out string handlerName)
    {
        handlerName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (MarkupExpressionEnvelopeSemantics.IsMarkupExpression(trimmed))
        {
            return false;
        }

        var segments = XamlMemberPathSemantics.SplitPathSegments(trimmed);
        if (segments.Length != 1)
        {
            return false;
        }

        var candidate = segments[0].Trim();
        if (candidate.Length == 0 ||
            candidate.IndexOfAny(['(', ')', '[', ']', ':']) >= 0)
        {
            return false;
        }

        if (!XamlIdentifierSemantics.TryNormalizeIdentifier(candidate, out var normalizedIdentifier))
        {
            return false;
        }

        handlerName = normalizedIdentifier;
        return true;
    }
}
