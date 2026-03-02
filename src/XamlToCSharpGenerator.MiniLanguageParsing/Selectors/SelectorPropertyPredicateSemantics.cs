using System;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public static class SelectorPropertyPredicateSemantics
{
    public static bool TrySplitPredicate(string predicateText, out string propertyText, out string valueText)
    {
        propertyText = string.Empty;
        valueText = string.Empty;
        if (string.IsNullOrWhiteSpace(predicateText))
        {
            return false;
        }

        var equalsIndex = TopLevelTextParser.IndexOfTopLevel(predicateText, '=');
        if (equalsIndex <= 0 || equalsIndex >= predicateText.Length - 1)
        {
            return false;
        }

        propertyText = predicateText.Substring(0, equalsIndex);
        valueText = predicateText.Substring(equalsIndex + 1);
        return propertyText.Length > 0 &&
               valueText.Length > 0 &&
               propertyText.Trim().Length == propertyText.Length;
    }

    public static SelectorAttachedPropertyParseKind TryParseAttachedPropertyToken(
        string propertyText,
        out string ownerTypeToken,
        out string propertyName)
    {
        ownerTypeToken = string.Empty;
        propertyName = string.Empty;

        if (string.IsNullOrWhiteSpace(propertyText))
        {
            return SelectorAttachedPropertyParseKind.NotAttached;
        }

        if (propertyText.Length == 0 || propertyText[0] != '(')
        {
            return SelectorAttachedPropertyParseKind.NotAttached;
        }

        var cursor = 0;
        if (!TopLevelTextParser.TryReadBalancedContent(
                propertyText,
                ref cursor,
                '(',
                ')',
                out var attachedText))
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        if (cursor != propertyText.Length)
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        if (!TrySplitOwnerAndProperty(attachedText, out var ownerToken, out var propertyToken))
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        if (!SelectorTokenSyntax.TryReadStandaloneTypeToken(ownerToken, out var parsedOwnerTypeToken) ||
            !SelectorTokenSyntax.IsValidIdentifier(propertyToken))
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        ownerTypeToken = parsedOwnerTypeToken;
        propertyName = propertyToken;
        return SelectorAttachedPropertyParseKind.ValidAttached;
    }

    private static bool TrySplitOwnerAndProperty(
        string attachedText,
        out string ownerToken,
        out string propertyToken)
    {
        ownerToken = string.Empty;
        propertyToken = string.Empty;
        if (string.IsNullOrWhiteSpace(attachedText))
        {
            return false;
        }

        var separator = FindLastTopLevelSeparator(attachedText, '.');
        if (separator <= 0 || separator >= attachedText.Length - 1)
        {
            return false;
        }

        ownerToken = attachedText.Substring(0, separator);
        propertyToken = attachedText.Substring(separator + 1);
        return ownerToken.Length > 0 &&
               propertyToken.Length > 0 &&
               ownerToken.Trim().Length == ownerToken.Length &&
               propertyToken.Trim().Length == propertyToken.Length;
    }

    private static int FindLastTopLevelSeparator(string value, char separator)
    {
        var roundDepth = 0;
        var squareDepth = 0;
        var curlyDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';
        var lastTopLevelSeparator = -1;

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];

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
                case '(':
                    roundDepth++;
                    continue;
                case ')':
                    if (roundDepth > 0)
                    {
                        roundDepth--;
                    }

                    continue;
                case '[':
                    squareDepth++;
                    continue;
                case ']':
                    if (squareDepth > 0)
                    {
                        squareDepth--;
                    }

                    continue;
                case '{':
                    curlyDepth++;
                    continue;
                case '}':
                    if (curlyDepth > 0)
                    {
                        curlyDepth--;
                    }

                    continue;
            }

            if (current == separator && roundDepth == 0 && squareDepth == 0 && curlyDepth == 0)
            {
                lastTopLevelSeparator = index;
            }
        }

        return lastTopLevelSeparator;
    }
}
