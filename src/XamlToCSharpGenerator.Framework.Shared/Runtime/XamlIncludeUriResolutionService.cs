using System;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Runtime;

public sealed class XamlIncludeUriResolutionService
{
    public static bool IsKnownMergeTarget(string? mergeTarget)
    {
        return mergeTarget switch
        {
            "MergedDictionaries" => true,
            "Styles" => true,
            _ => false
        };
    }

    public bool TryResolveIncludeUri(
        string includeSource,
        string currentTargetPath,
        string currentDocumentUri,
        IXamlFrameworkDocumentUriResolver documentUriResolver,
        out string resolvedUri,
        out bool isProjectLocal)
    {
        resolvedUri = string.Empty;
        isProjectLocal = false;

        var normalizedSource = NormalizeIncludeSource(includeSource);
        if (string.IsNullOrWhiteSpace(normalizedSource))
        {
            return false;
        }

        return documentUriResolver.TryResolveIncludeUri(
            normalizedSource,
            currentTargetPath,
            currentDocumentUri,
            out resolvedUri,
            out isProjectLocal);
    }

    public string NormalizeIncludeSource(string includeSource)
    {
        var trimmed = includeSource.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return trimmed;
        }

        var separatorIndex = inner.IndexOfAny(new[] { ' ', ',' });
        var markupName = separatorIndex >= 0
            ? inner.Substring(0, separatorIndex)
            : inner;
        if (!markupName.Equals("x:Uri", StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("Uri", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var arguments = separatorIndex >= 0
            ? inner.Substring(separatorIndex + 1).Trim()
            : string.Empty;
        if (arguments.Length == 0)
        {
            return trimmed;
        }

        var namedUriIndex = arguments.IndexOf("Uri=", StringComparison.OrdinalIgnoreCase);
        if (namedUriIndex >= 0)
        {
            var namedValue = arguments.Substring(namedUriIndex + "Uri=".Length).TrimStart();
            return TryExtractQuotedOrBareValue(namedValue, out var extractedNamedValue)
                ? extractedNamedValue
                : trimmed;
        }

        return TryExtractQuotedOrBareValue(arguments, out var extractedPositionalValue)
            ? extractedPositionalValue
            : trimmed;
    }

    private static bool TryExtractQuotedOrBareValue(string rawValue, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed[0] == '\'' || trimmed[0] == '"')
        {
            var quote = trimmed[0];
            var closingQuoteIndex = trimmed.IndexOf(quote, 1);
            if (closingQuoteIndex <= 0)
            {
                return false;
            }

            value = trimmed.Substring(1, closingQuoteIndex - 1).Trim();
            return value.Length > 0;
        }

        var commaIndex = trimmed.IndexOf(',');
        value = (commaIndex >= 0 ? trimmed.Substring(0, commaIndex) : trimmed).Trim();
        return value.Length > 0;
    }
}
