using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlPropertyElementSemantics
{
    public static bool TrySplitOwnerQualifiedPropertyFragment(
        string propertyToken,
        out string ownerToken,
        out string propertyNameFragment)
    {
        ownerToken = string.Empty;
        propertyNameFragment = string.Empty;

        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var trimmed = propertyToken.Trim();
        var separatorIndex = trimmed.LastIndexOf('.');
        if (separatorIndex <= 0)
        {
            return false;
        }

        ownerToken = trimmed.Substring(0, separatorIndex).Trim();
        propertyNameFragment = separatorIndex + 1 < trimmed.Length
            ? trimmed.Substring(separatorIndex + 1).Trim()
            : string.Empty;

        return ownerToken.Length > 0;
    }

    public static bool IsOwnerSegmentOffset(string propertyToken, int relativeOffset)
    {
        if (string.IsNullOrWhiteSpace(propertyToken) || relativeOffset < 0)
        {
            return false;
        }

        var separatorIndex = propertyToken.LastIndexOf('.');
        return separatorIndex > 0 && relativeOffset < separatorIndex;
    }

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
