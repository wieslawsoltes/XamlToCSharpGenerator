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
        if (!SelectorPropertyPredicateSemantics.TrySplitPredicate(predicateText, out var propertyText, out var valueText))
        {
            return false;
        }

        var attachedParseKind = SelectorPropertyPredicateSemantics.TryParseAttachedPropertyToken(
            propertyText,
            out var attachedOwnerTypeToken,
            out var attachedPropertyName);
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
        return SelectorPropertyPredicateSemantics.TrySplitPredicate(predicateText, out propertyText, out valueText);
    }

    public static SelectorAttachedPropertyParseKind TryParseAttachedProperty(
        string propertyText,
        out string ownerTypeToken,
        out string propertyName)
    {
        return SelectorPropertyPredicateSemantics.TryParseAttachedPropertyToken(
            propertyText,
            out ownerTypeToken,
            out propertyName);
    }
}
