using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlAvaloniaCursorLiteralSemantics
{
    public static bool TryParseStandardCursorTypeMember(string? value, out string memberName)
    {
        memberName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var token = value!.Trim();
        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                token,
                out var ownerToken,
                out var memberToken))
        {
            if (!ownerToken.Equals("StandardCursorType", StringComparison.OrdinalIgnoreCase) &&
                !ownerToken.Equals("CursorType", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            token = memberToken.Trim();
        }

        return XamlIdentifierSemantics.TryNormalizeIdentifier(token, out memberName);
    }
}
