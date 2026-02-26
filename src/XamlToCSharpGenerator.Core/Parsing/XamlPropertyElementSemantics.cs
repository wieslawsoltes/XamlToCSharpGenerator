using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlPropertyElementSemantics
{
    public static bool IsPropertyElementName(string localName)
    {
        if (string.IsNullOrWhiteSpace(localName))
        {
            return false;
        }

        return XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
            localName,
            out _,
            out _);
    }

    public static bool IsAttachedPropertyToken(string propertyToken)
    {
        return IsPropertyElementName(propertyToken);
    }
}
