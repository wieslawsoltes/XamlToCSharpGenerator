using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlPropertyTokenSemantics
{
    public static bool TrySplitOwnerQualifiedProperty(
        string propertyToken,
        out string ownerToken,
        out string propertyName)
    {
        ownerToken = string.Empty;
        propertyName = string.Empty;

        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        return XamlTokenSplitSemantics.TrySplitAtLastSeparator(
            propertyToken,
            '.',
            out ownerToken,
            out propertyName);
    }

    public static bool IsPropertyElementName(
        string localName,
        string propertyName,
        string? ownerToken = null)
    {
        if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (TrySplitOwnerQualifiedProperty(localName, out var candidateOwner, out var candidateProperty))
        {
            if (!candidateProperty.Equals(propertyName, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(ownerToken))
            {
                return true;
            }

            return candidateOwner.Equals(ownerToken, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(ownerToken))
        {
            return false;
        }

        return localName.Equals(propertyName, StringComparison.Ordinal);
    }
}
