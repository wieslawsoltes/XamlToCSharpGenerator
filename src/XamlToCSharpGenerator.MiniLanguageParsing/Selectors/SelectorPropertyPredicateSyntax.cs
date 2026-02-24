using System;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public enum SelectorAttachedPropertyParseKind
{
    NotAttached = 0,
    ValidAttached = 1,
    InvalidAttached = 2,
}

public readonly struct SelectorPropertyPredicate
{
    public SelectorPropertyPredicate(string propertyToken, string rawValue)
    {
        PropertyToken = propertyToken;
        RawValue = rawValue;
    }

    public string PropertyToken { get; }

    public string RawValue { get; }
}

public static class SelectorPropertyPredicateSyntax
{
    public static bool TryParse(string predicateText, out SelectorPropertyPredicate predicate)
    {
        predicate = default;
        if (!TrySplit(predicateText, out var propertyText, out var valueText))
        {
            return false;
        }

        var attachedParseKind = TryParseAttachedProperty(propertyText, out var attachedOwnerTypeToken, out var attachedPropertyName);
        if (attachedParseKind == SelectorAttachedPropertyParseKind.InvalidAttached)
        {
            return false;
        }

        var propertyToken = attachedParseKind == SelectorAttachedPropertyParseKind.ValidAttached
            ? attachedOwnerTypeToken + "." + attachedPropertyName
            : propertyText.Replace('|', ':');
        predicate = new SelectorPropertyPredicate(propertyToken, valueText);
        return propertyToken.Length > 0;
    }

    public static bool TrySplit(string predicateText, out string propertyText, out string valueText)
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

        propertyText = predicateText.Substring(0, equalsIndex).Trim();
        valueText = predicateText.Substring(equalsIndex + 1).Trim();
        return propertyText.Length > 0 && valueText.Length > 0;
    }

    public static SelectorAttachedPropertyParseKind TryParseAttachedProperty(
        string propertyText,
        out string ownerTypeToken,
        out string propertyName)
    {
        ownerTypeToken = string.Empty;
        propertyName = string.Empty;

        if (!propertyText.StartsWith("(", StringComparison.Ordinal) ||
            !propertyText.EndsWith(")", StringComparison.Ordinal))
        {
            return SelectorAttachedPropertyParseKind.NotAttached;
        }

        if (propertyText.Length <= 2)
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        var attachedText = propertyText.Substring(1, propertyText.Length - 2).Trim();
        var separator = attachedText.LastIndexOf('.');
        if (separator <= 0 || separator >= attachedText.Length - 1)
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        var ownerToken = attachedText.Substring(0, separator).Trim();
        var propertyToken = attachedText.Substring(separator + 1).Trim();
        if (!SelectorTokenSyntax.TryReadStandaloneTypeToken(ownerToken, out var parsedOwnerTypeToken) ||
            !SelectorTokenSyntax.IsValidIdentifier(propertyToken))
        {
            return SelectorAttachedPropertyParseKind.InvalidAttached;
        }

        ownerTypeToken = parsedOwnerTypeToken;
        propertyName = propertyToken;
        return SelectorAttachedPropertyParseKind.ValidAttached;
    }
}
